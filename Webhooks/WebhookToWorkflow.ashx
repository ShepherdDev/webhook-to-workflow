<%@ WebHandler Language="C#" Class="WebhookToWorkflow" %>

using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.IO;
using System.Linq;
using System.Web;
using System.Text;
using System.Text.RegularExpressions;
using Rock;
using Rock.Data;
using Rock.Model;

public class WebhookToWorkflow : IHttpHandler
{
    public void ProcessRequest( HttpContext context )
    {
        string url = "/" + string.Join( "", context.Request.Url.Segments.SkipWhile( s => !s.StartsWith( "WebhookToWorkflow.ashx", StringComparison.InvariantCultureIgnoreCase ) ).Skip( 1 ).ToArray() );
        string body = string.Empty;
        RockContext rockContext = new RockContext();
        DefinedType hooks = new DefinedTypeService( rockContext ).Get( 327 );
        DefinedValue hook = null;

        context.Response.ContentType = "text/plain";

        foreach ( DefinedValue h in hooks.DefinedValues.OrderBy( h => h.Order ) )
        {
            bool urlMatch = false;
            string hookUrl;
            string hookMethod;

            h.LoadAttributes();
            hookUrl = h.GetAttributeValue( "Url" );
            hookMethod = h.GetAttributeValue( "Method" );

            //
            // Check for match on method type, if not continue to the next item.
            //
            if ( !string.IsNullOrEmpty( hookMethod ) && !context.Request.HttpMethod.ToString().Equals( hookMethod, StringComparison.InvariantCultureIgnoreCase ) )
            {
                continue;
            }

            //
            // Check for match on the URL.
            //
            if ( string.IsNullOrEmpty( hookUrl ) )
            {
                urlMatch = true;
            }
            else if ( hookUrl.StartsWith( "^" ) && hookUrl.EndsWith( "$" ) )
            {
                urlMatch = Regex.IsMatch( url, hookUrl, RegexOptions.IgnoreCase );
            }
            else
            {
                urlMatch = url.Equals( hookUrl, StringComparison.InvariantCultureIgnoreCase );
            }

            if ( urlMatch )
            {
                hook = h;
                break;
            }
        }

        //
        // If we have a hook then try to find the workflow information.
        //
        if ( hook != null )
        {
            Guid guid = hook.GetAttributeValue( "WorkflowType" ).AsGuid();
            WorkflowType workflowType = new WorkflowTypeService( rockContext ).Get( guid );
            if ( workflowType != null )
            {
                Workflow workflow = Workflow.Activate( workflowType, context.Request.UserHostName );
                if ( workflow != null )
                {
                    List<string> errorMessages;

                    workflow.LoadAttributes();
                    workflow.SetAttributeValue( "Request", RequestToJson( context.Request, hook, url ).ToString() );
                    new WorkflowService( rockContext ).Process( workflow, out errorMessages );

                    context.Response.StatusCode = 200;
                    context.Response.End();

                    return;
                }
            }
        }

        //
        // If we got here then something went wrong, probably we couldn't find a matching hook.
        //
        context.Response.StatusCode = 404;
        context.Response.Write( "Path not found." );
        context.Response.End();
    }

    public string RequestToJson( HttpRequest request, DefinedValue hook, string url )
    {
        var dictionary = new Dictionary<string, object>();

        //
        // Set the standard values to be used.
        //
        dictionary.Add( "DefinedValueId", hook.Id );
        dictionary.Add( "Url", url );
        dictionary.Add( "RawUrl", request.Url.AbsoluteUri );
        dictionary.Add( "Method", request.HttpMethod );
        dictionary.Add( "QueryString", request.QueryString.Cast<string>().ToDictionary( q => q, q => request.QueryString[q] ) );
        dictionary.Add( "RemoteAddress", request.UserHostAddress );
        dictionary.Add( "RemoteName", request.UserHostName );
        dictionary.Add( "ServerName", request.Url.Host );

        //
        // Add in the raw body content.
        //
        using ( StreamReader reader = new StreamReader( request.InputStream, Encoding.UTF8 ) )
        {
            dictionary.Add( "RawBody", reader.ReadToEnd() );
        }

        //
        // Parse the body content if it is JSON or standard Form data.
        //
        if ( request.ContentType == "application/json" )
        {
            try
            {
                dictionary.Add( "Body", Newtonsoft.Json.JsonConvert.DeserializeObject( ( string )dictionary["RawBody"] ) );
            }
            catch
            {
            }
        }
        else if ( request.ContentType == "application/x-www-form-urlencoded" )
        {
            try
            {
                dictionary.Add( "Body", request.Form.Cast<string>().ToDictionary( q => q, q => request.Form[q] ) );
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
            var headers = request.Headers.Cast<string>()
                .Where( h => !h.Equals( "Authorization", StringComparison.InvariantCultureIgnoreCase ) )
                .Where( h => !h.Equals( "Cookie", StringComparison.InvariantCultureIgnoreCase ) )
                .ToDictionary( h => h, h => request.Headers[h] );
            dictionary.Add( "Headers", headers );
        }

        //
        // Add in all the cookies if the admin wants them.
        //
        if ( hook.GetAttributeValue( "Cookies" ).AsBoolean() )
        {
            dictionary.Add( "Cookies", request.Cookies.Cast<string>().ToDictionary( q => q, q => request.Cookies[q].Value ) );
        }

        return Newtonsoft.Json.JsonConvert.SerializeObject( dictionary );
    }

    private void WriteToLog( string message )
    {
        string logFile = HttpContext.Current.Server.MapPath( "~/App_Data/Logs/IncomingWebhook.txt" );

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
                        sw.WriteLine( string.Format( "{0} - {1}", Rock.RockDateTime.Now.ToString(), message ) );
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

    public bool IsReusable
    {
        get
        {
            return false;
        }
    }
}
