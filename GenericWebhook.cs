﻿using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.IO;
using System.Linq;
using System.Web;
using System.Text;
using System.Text.RegularExpressions;

using Newtonsoft.Json;

using Rock;
using Rock.Data;
using Rock.Model;

namespace com.shepherdchurch.WebhookToWorkflow
{
    /// <summary>
    /// Generic webhook to workflow implementation. Does basic decoding of FORM data
    /// and JSON data and provides basic HttpRequest information to the Workflow.
    /// </summary>
    public partial class GenericWebhook : IHttpHandler
    {
        /// <summary>
        /// The HttpContext related to this processing instance.
        /// </summary>
        protected HttpContext HttpContext { get; private set; }

        /// <summary>
        /// The RockContext that has been setup for reading objects from the database
        /// for this instance.
        /// </summary>
        protected RockContext RockContext { get; private set; }

        /// <summary>
        /// The partial URL of this request instance. This will always begin with a slash
        /// and contain all path elements after the .ashx file.
        /// </summary>
        protected string Url { get; private set; }

        /// <summary>
        /// Process the incoming http request. This is the web handler entry point.
        /// </summary>
        /// <param name="context">The context that contains all information about this request.</param>
        public void ProcessRequest( HttpContext context )
        {
            DefinedValue hook;

            try
            {
                //
                // Set the instance variables for subclasses to be able to use.
                //
                RockContext = new RockContext();
                HttpContext = context;
                Url = "/" + string.Join( "", HttpContext.Request.Url.Segments.SkipWhile( s => !s.EndsWith( ".ashx", StringComparison.InvariantCultureIgnoreCase ) && !s.EndsWith( ".ashx/", StringComparison.InvariantCultureIgnoreCase ) ).Skip( 1 ).ToArray() );

                //
                // Find the hook for this type of instance.
                //
                hook = GetHookForRequest( DefinedTypeGuid() );

                //
                // If we have a hook then try to find the workflow information.
                //
                if ( hook != null )
                {
                    Guid guid = hook.GetAttributeValue( "WorkflowType" ).AsGuid();
                    var workflowType = Rock.Web.Cache.WorkflowTypeCache.Get( guid );

                    if ( workflowType != null )
                    {
                        Workflow workflow = Workflow.Activate( workflowType, context.Request.UserHostName );

                        //
                        // We found a workflow type and were able to instantiate a new one.
                        //
                        if ( workflow != null )
                        {
                            List<string> errorMessages;

                            //
                            // Load in all the attributes that are defined in the workflow.
                            //
                            workflow.LoadAttributes();
                            PopulateWorkflowAttributes( workflow, hook );

                            //
                            // Execute the workflow.
                            //
                            new WorkflowService( RockContext ).Process( workflow, out errorMessages );

                            //
                            // We send a response (if one is available) wether the workflow has ended
                            // or not. This gives them a chance to send a "let me work on that for you"
                            // type response and then continue processing in the background.
                            //
                            SendWorkflowResponse( workflow, hook );

                            context.Response.End();

                            return;
                        }
                    }
                }

                //
                // If we got here then something went wrong, probably we couldn't find a matching hook.
                //
                context.Response.ContentType = "text/plain";
                context.Response.StatusCode = 404;
                context.Response.Write( "Path not found." );
                context.Response.End();
            }
            catch ( Exception e )
            {
                WriteToLog( e.Message );
            }
        }

        /// <summary>
        /// These webhooks are not reusable and must only be used once.
        /// </summary>
        public bool IsReusable
        {
            get
            {
                return false;
            }
        }

        #region Methods for subclass override

        /// <summary>
        /// The GUID of the DefinedType to consider for matching webhooks. This
        /// should be overridden by subclasses to use their own DefinedType GUID.
        /// </summary>
        /// <returns>A Guid object which will identify the DefinedType.</returns>
        protected virtual Guid DefinedTypeGuid()
        {
            return new Guid( "dd5ba760-9942-4274-8b86-08691637e167" );
        }

        /// <summary>
        /// Check if this DefinedValue is valid for the current request. Subclasses should
        /// call the base method so that the Method and Url are verified.
        /// </summary>
        /// <param name="hook">The DefinedValue that is to be considered for this request.</param>
        /// <returns>true if the DefinedValue matches this request, otherwise false.</returns>
        protected virtual bool IsHookValidForRequest( DefinedValue hook )
        {
            string hookUrl = hook.GetAttributeValue( "Url" );
            string hookMethod = hook.GetAttributeValue( "Method" );

            //
            // Check for match on method type, if not continue to the next item.
            //
            if ( !string.IsNullOrEmpty( hookMethod ) && !HttpContext.Request.HttpMethod.ToString().Equals( hookMethod, StringComparison.InvariantCultureIgnoreCase ) )
            {
                return false;
            }

            //
            // Check for match on the URL.
            //
            if ( string.IsNullOrEmpty( hookUrl ) )
            {
                return true;
            }
            else if ( hookUrl.StartsWith( "^" ) && hookUrl.EndsWith( "$" ) )
            {
                return Regex.IsMatch( Url, hookUrl, RegexOptions.IgnoreCase );
            }
            else
            {
                return Url.Equals( hookUrl, StringComparison.InvariantCultureIgnoreCase );
            }
        }

        /// <summary>
        /// Populates any defined Workflow attributes specified to this webhook type.
        /// Subclasses may call the base method to have the Request attribute set.
        /// </summary>
        /// <param name="workflow">The workflow whose attributes need to be set.</param>
        /// <param name="hook">The DefinedValue of the currently executing webhook.</param>
        protected virtual void PopulateWorkflowAttributes( Workflow workflow, DefinedValue hook )
        {
            workflow.SetAttributeValue( "Request", JsonConvert.SerializeObject( RequestToDictionary( hook) ) );
        }

        /// <summary>
        /// Send a response to the current request. By default anything in the Response
        /// workflow attribute is sent as text/plain content. Subclasses should override
        /// this method if they want a different type of response sent, or need to ensure
        /// that no response is ever sent.
        /// </summary>
        /// <param name="workflow">The workflow that can the response data.</param>
        /// <param name="hook">The DefinedValue of the currently executing webhook.</param>
        protected virtual void SendWorkflowResponse( Workflow workflow, DefinedValue hook )
        {
            string response = workflow.GetAttributeValue( "Response" );
            string contentType = workflow.GetAttributeValue( "ContentType" );

            HttpContext.Response.ContentType = "text/plain";

            if ( !string.IsNullOrEmpty( response ) )
            {
                HttpContext.Response.Write( response );
            }

            if ( !string.IsNullOrWhiteSpace( contentType ) )
            {
                HttpContext.Response.ContentType = contentType;
            }
        }

        /// <summary>
        /// Convert the request into a generic JSON object that can provide information
        /// to the workflow. If a subclass does needs to customize this data they can
        /// call the base method and then modify the content before returning it.
        /// </summary>
        /// <param name="hook">The DefinedValue of the currently executing webhook.</param>
        /// <returns></returns>
        protected virtual Dictionary<string, object> RequestToDictionary( DefinedValue hook )
        {
            var dictionary = new Dictionary<string, object>();

            //
            // Set the standard values to be used.
            //
            dictionary.Add( "DefinedValueId", hook.Id );
            dictionary.Add( "Url", Url );
            dictionary.Add( "RawUrl", HttpContext.Request.Url.AbsoluteUri );
            dictionary.Add( "Method", HttpContext.Request.HttpMethod );
            dictionary.Add( "QueryString", HttpContext.Request.QueryString.Cast<string>().ToDictionary( q => q, q => HttpContext.Request.QueryString[q] ) );
            dictionary.Add( "RemoteAddress", HttpContext.Request.UserHostAddress );
            dictionary.Add( "RemoteName", HttpContext.Request.UserHostName );
            dictionary.Add( "ServerName", HttpContext.Request.Url.Host );

            //
            // Add in the raw body content.
            //
            using ( StreamReader reader = new StreamReader( HttpContext.Request.InputStream, Encoding.UTF8 ) )
            {
                dictionary.Add( "RawBody", reader.ReadToEnd() );
            }

            //
            // Parse the body content if it is JSON or standard Form data.
            //
            if ( HttpContext.Request.ContentType == "application/json" )
            {
                try
                {
                    dictionary.Add( "Body", Newtonsoft.Json.JsonConvert.DeserializeObject( ( string )dictionary["RawBody"] ) );
                }
                catch
                {
                }
            }
            else if ( HttpContext.Request.ContentType == "application/x-www-form-urlencoded" )
            {
                try
                {
                    dictionary.Add( "Body", HttpContext.Request.Form.Cast<string>().ToDictionary( q => q, q => HttpContext.Request.Form[q] ) );
                }
                catch
                {
                }
            }

            //
            // Add in all the headers if the admin wants them.
            //
            if ( hook.GetAttributeValue( "Headers" ).AsBoolean() )
            {
                var headers = HttpContext.Request.Headers.Cast<string>()
                    .Where( h => !h.Equals( "Authorization", StringComparison.InvariantCultureIgnoreCase ) )
                    .Where( h => !h.Equals( "Cookie", StringComparison.InvariantCultureIgnoreCase ) )
                    .ToDictionary( h => h, h => HttpContext.Request.Headers[h] );
                dictionary.Add( "Headers", headers );
            }

            //
            // Add in all the cookies if the admin wants them.
            //
            if ( hook.GetAttributeValue( "Cookies" ).AsBoolean() )
            {
                dictionary.Add( "Cookies", HttpContext.Request.Cookies.Cast<string>().ToDictionary( q => q, q => HttpContext.Request.Cookies[q].Value ) );
            }

            return dictionary;
        }

        #endregion

        #region Support methods

        /// <summary>
        /// Retrieve the DefinedValue for this request by matching the Method, Url
        /// and any other filters defined by subclasses.
        /// </summary>
        /// <param name="definedTypeGuid">The GUID of the DefinedType whose values should be considered.</param>
        /// <returns>A DefinedValue for the webhook request that was matched or null if one was not found.</returns>
        protected DefinedValue GetHookForRequest( Guid definedTypeGuid )
        {
            DefinedType hooks = new DefinedTypeService( RockContext ).Get( definedTypeGuid );

            foreach ( DefinedValue hook in hooks.DefinedValues.OrderBy( h => h.Order ) )
            {
                hook.LoadAttributes();

                if ( IsHookValidForRequest( hook ) )
                {
                    return hook;
                }
            }

            return null;
        }

        /// <summary>
        /// Log a message to the WebhookToWorkflow.txt file. The message is prefixed by
        /// the date and the class name.
        /// </summary>
        /// <param name="message">The message to be logged.</param>
        protected void WriteToLog( string message )
        {
            string logFile = HttpContext.Current.Server.MapPath( "~/App_Data/Logs/WebhookToWorkflow.txt" );

            // Write to the log, but if an ioexception occurs wait a couple seconds and then try again (up to 3 times).
            var maxRetry = 3;
            for ( int retry = 0; retry < maxRetry; retry++ )
            {
                try
                {
                    using ( FileStream fs = new FileStream( logFile, FileMode.Append, FileAccess.Write ) )
                    {
                        using ( StreamWriter sw = new StreamWriter( fs ) )
                        {
                            sw.WriteLine( string.Format( "{0} [{2}] - {1}", RockDateTime.Now.ToString(), message, GetType().Name ) );
                            break;
                        }
                    }
                }
                catch ( IOException )
                {
                    if ( retry < maxRetry - 1 )
                    {
                        System.Threading.Thread.Sleep( 2000 );
                    }
                }
            }

        }

        #endregion
    }
}
