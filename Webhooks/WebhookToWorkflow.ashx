<%@ WebHandler Language="C#" Class="WebhookToWorkflow" %>

using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.IO;
using System.Linq;
using System.Web;
using System.Text;
using Rock;
using Rock.Data;
using Rock.Model;

public class WebhookToWorkflow: IHttpHandler
{
    public void ProcessRequest( HttpContext context )
    {
        string url = "/" + string.Join( "", context.Request.Url.Segments.SkipWhile( s => !s.StartsWith( "WebhookToWorkflow.ashx", StringComparison.InvariantCultureIgnoreCase ) ).Skip(1).ToArray() );
        string body = string.Empty;
        RockContext rockContext = new RockContext();
        DefinedType hooks = new DefinedTypeService( rockContext ).Get( 327 );
        DefinedValue hook = null;

        context.Response.ContentType = "text/plain";

        foreach (DefinedValue h in hooks.DefinedValues.OrderBy(h => h.Order))
        {
            h.LoadAttributes();

            //
            // Check for match on URL, if not continue to the next item.
            //
            if ( !string.IsNullOrEmpty( h.GetAttributeValue( "Url" ) ) && !url.Equals( h.GetAttributeValue( "Url" ), StringComparison.InvariantCultureIgnoreCase ) )
            {
                continue;
            }

            //
            // Check for match on method type, if not continue to the next item.
            //
            if ( !string.IsNullOrEmpty( h.GetAttributeValue( "Method" ) ) && !context.Request.HttpMethod.ToString().Equals( h.GetAttributeValue( "Method" ), StringComparison.InvariantCultureIgnoreCase ) )
            {
                continue;
            }

            hook = h;
            break;
        }

        if ( hook == null )
        {
            context.Response.StatusCode = 404;
            context.Response.Write( "Path not found." );
            return;
        }

        Guid guid = hook.GetAttributeValue( "WorkflowType" ).AsGuid();
        WorkflowType workflowType = new WorkflowTypeService( rockContext ).Get( guid );
        if ( workflowType != null )
        {
            Workflow workflow = Workflow.Activate( workflowType, context.Request.UserHostName );
            if ( workflow != null )
            {
                List<string> errorMessages;

                workflow.LoadAttributes();
                workflow.SetAttributeValue( "Request", new RequestData(context.Request, hook, url ).ToString() );
                new WorkflowService( rockContext ).Process( workflow, out errorMessages );
            }
        }

        context.Response.StatusCode = 200;
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

public class RequestData
{
    public HttpRequest Request { get; set; }
    public DefinedValue Hook { get; set; }
    public string Url { get; set; }

    public RequestData( HttpRequest request, DefinedValue hook, string url )
    {
        Request = request;
        Hook = hook;
        Url = url;
    }

    public override string ToString()
    {
        var dictionary = new Dictionary<string, object>();

        dictionary.Add( "DefinedValueId", Hook.Id );
        dictionary.Add( "Url", Url );
        dictionary.Add( "RawUrl", Request.Url.AbsoluteUri );
        dictionary.Add( "Method", Request.HttpMethod );
        dictionary.Add( "QueryString", Request.QueryString.Cast<string>().ToDictionary( q => q, q => Request.QueryString[q] ) );
        dictionary.Add( "RemoteAddress", Request.UserHostAddress );
        dictionary.Add( "RemoteName", Request.UserHostName );
        dictionary.Add( "ServerName", Request.Url.Host );

        using ( StreamReader reader = new StreamReader( Request.InputStream, Encoding.UTF8 ) )
        {
            dictionary.Add( "RawBody", reader.ReadToEnd() );
        }

            // TODO: Decode form data.
        if ( Request.ContentType == "application/json" )
        {
            try
            {
                dictionary.Add( "Body", Newtonsoft.Json.JsonConvert.DeserializeObject( (string)dictionary["RawBody"] ) );
            }
            catch
            {
            }
        }

        if ( Hook.GetAttributeValue( "Headers" ).AsBoolean() )
        {
            var headers = Request.Headers.Cast<string>()
                .Where( h => !h.Equals( "Authorization", StringComparison.InvariantCultureIgnoreCase ) )
                .Where( h => !h.Equals( "Cookie", StringComparison.InvariantCultureIgnoreCase ) )
                .ToDictionary( h => h, h => Request.Headers[h] );
            dictionary.Add( "Headers", headers );
        }

        if ( Hook.GetAttributeValue( "Cookies" ).AsBoolean() )
        {
            dictionary.Add( "Cookies", Request.Cookies.Cast<string>().ToDictionary( q => q, q => Request.Cookies[q].Value ) );
        }

        return Newtonsoft.Json.JsonConvert.SerializeObject( dictionary );
    }
}
