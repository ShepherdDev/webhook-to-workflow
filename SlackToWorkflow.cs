using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

using Newtonsoft.Json;
using Rock.Model;

namespace com.shepherdchurch.WebhookToWorkflow
{
    class SlackToWorkflow : GenericWebhook
    {
        /// <summary>
        /// The GUID that identifies the DefinedType for the Slack interface.
        /// </summary>
        /// <returns>A Guid object</returns>
        protected override Guid DefinedTypeGuid()
        {
            return new Guid( "751e5eb6-a02d-4836-9382-7eac6280fa4c" );
        }

        /// <summary>
        /// Check if this DefinedValue's filters apply to this request.
        /// </summary>
        /// <param name="hook">The DefinedValue to consider for filtering.</param>
        /// <returns>true if the DefinedValue should be used, false if not.</returns>
        protected override bool IsHookValidForRequest( DefinedValue hook )
        {
            if ( !base.IsHookValidForRequest( hook ) )
            {
                return false;
            }

            //
            // Check if the text coming from slack matches the text filter. If the
            // filter begins with ^ and ends with $ it is treated as a regex match.
            //
            string text = hook.GetAttributeValue( "Text" );
            if ( !string.IsNullOrWhiteSpace( text ) )
            {
                if ( text.StartsWith( "^" ) && text.EndsWith( "^" ) )
                {
                    if ( !Regex.IsMatch( HttpContext.Request.Form["text"], text, RegexOptions.IgnoreCase ) )
                    {
                        return false;
                    }
                }
                else
                {
                    if ( HttpContext.Request.Form["text"].IndexOf( text, StringComparison.OrdinalIgnoreCase ) < 0 )
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        /// <summary>
        /// Populate all the Slack specific attributes into the workflow.
        /// </summary>
        /// <param name="workflow">The workflow whose attributes need to be filled in.</param>
        /// <param name="hook">The DefinedValue that matched this request.</param>
        protected override void PopulateWorkflowAttributes( Workflow workflow, DefinedValue hook )
        {
            base.PopulateWorkflowAttributes( workflow, hook );

            workflow.SetAttributeValue( "Team", HttpContext.Request.Form["team_domain"] );
            workflow.SetAttributeValue( "Channel", HttpContext.Request.Form["channel_name"] );
            workflow.SetAttributeValue( "Username", HttpContext.Request.Form["user_name"] );
            workflow.SetAttributeValue( "Text", HttpContext.Request.Form["text"] );
            workflow.SetAttributeValue( "Trigger", HttpContext.Request.Form["trigger_word"] );
        }

        /// <summary>
        /// Send a JSON response to the Slack system if one was provided by the
        /// workflow. If the Response attribute value can be convert into JSON then
        /// it will be sent, otherwise it is assumed to be plain text and it is wrapped
        /// in a JSON object. If the JSON does not contain a "username" property and
        /// a "Username" attribute for the DefinedValue has been defined then it will
        /// be inserted into the JSON object.
        /// </summary>
        /// <param name="workflow">The workflow to check for a Response value in.</param>
        /// <param name="hook">The DefinedValue which identifies this webhook.</param>
        protected override void SendWorkflowResponse( Workflow workflow, DefinedValue hook )
        {
            string response = workflow.GetAttributeValue( "Response" );

            HttpContext.Response.ContentType = "application/json";

            if ( !string.IsNullOrEmpty( response ) )
            {
                try
                {
                    var json = JsonConvert.DeserializeObject<Dictionary<string, object>>( response );

                    AddSlackUsernameAndIcon( hook, json );

                    HttpContext.Response.Write( JsonConvert.SerializeObject( json ) );
                }
                catch
                {
                    Dictionary<string, object> json = new Dictionary<string, object>();

                    json.Add( "text", response );
                    AddSlackUsernameAndIcon( hook, json );

                    HttpContext.Response.Write( JsonConvert.SerializeObject( json ) );
                }
            }
        }

        /// <summary>
        /// Attach the default username and icon's to the outgoing Slack Message if they have not
        /// already been defined by the workflow.
        /// </summary>
        /// <param name="hook">The DefinedValue which identifies this webhook.</param>
        /// <param name="json">the data that contains the slack message.</param>
        private void AddSlackUsernameAndIcon( DefinedValue hook, Dictionary<string, object> json )
        {
            if ( !json.ContainsKey( "username" ) && !string.IsNullOrWhiteSpace( hook.GetAttributeValue( "Username" ) ) )
            {
                json.Add( "username", hook.GetAttributeValue( "Username" ) );
            }

            if ( !json.ContainsKey( "icon_url" ) && !json.ContainsKey( "icon_emojoi" ) )
            {
                if ( !string.IsNullOrWhiteSpace( hook.GetAttributeValue( "Icon" ) ) )
                {
                    json.Add( "icon_url", hook.GetAttributeValue( "Icon" ) );
                }
            }
        }
    }
}
