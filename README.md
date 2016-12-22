## Webhook To Workflow

This webhook provides a very generic method of accepting an incoming
webhook and firing off workflow in response. Minimal security is
optionally provided so care should be taken with your workflows as
this would be a great way for somebody to perform a Denial-of-Service
attack on your server by firing off hundreds of workflows by repeatedly
hitting the URL.

Currently the only security is the ability to list IP addresses and
subnets that are allowed to use the Workflow. You may enter one or
more IP addresses seprated by a `,`. You can also optionally provide
a netmask in `/32` notation. For example to allow `192.168.*.*` to
use a Workflow you could enter `192.168.0.0/16`. If you do not
provide a subnet mask then `/32` is assumed, which means only that
specific IP address is matched.

### Workflow `Request` Attribute

If the Workflow contains an attribute called `Request` then it will
be stuffed with a JSON encoded object in string format that will
provide your Workflow with information about the request. You can
use this information to decide how best to process the request.

Sending a `POST` with the body content of `{"name":"Daniel"}`
to the URL `http://rock.example.com/Webhooks/WebhookToWorkflow.ashx/full?groupId=42`
will cause the following JSON to be encoded as a string and placed
in the `Request` Workflow attribute.

```json
{
  "DefinedValueId": 5651,
  "Url": "\/full",
  "RawUrl": "http:\/\/localhost:64706\/Webhooks\/WebhookToWorkflow.ashx\/full?groupId=42",
  "Method": "POST",
  "QueryString": {
    "groupId": "42"
  },
  "RemoteAddress": "127.0.0.1",
  "RemoteName": "localhost",
  "ServerName": "localhost",
  "Body": {
    "name": "Daniel"
  },
  "RawBody": "{\"name\":\"Daniel\"}",
  "Headers": {
    "Content-Length": "17",
    "Content-Type": "application\/x-www-form-urlencoded",
    "Accept": "*\/*",
    "Host": "localhost:64706",
    "User-Agent": "curl\/7.35.0"
  },
  "Cookies": {
    "USER_TOKEN": "Yes",
    "IsValid": "No"
  }
}
```

> Note: Because this is JSON data some of the values may look a
> little strange. For example the `Url` value looks like `\/full`
> but once you run it through the FromJSON filter it will be its
> true value of just `/full`.

You can use the `FromJSON` Lava filter to convert the JSON into an
object that can be used to retrieve specific values. A few examples.

If you wanted to get the RemoteAddress of the request you could use
the following Lava in the Workflow:

```
{% assign request = Workflow | Attribute:'Request' | FromJSON -%}
{{ request.RemoteAddress }}
```

You may have noticed that the body we sent was a JSON string. This
webhook will automatically decode x-www-form-urnencoded data as well
as json data. If you are getting other formatted data you can access
it via the `RawBody` property. Otherwise you may access the decoded
data in the `Body` property. So if we wanted to get to the `name`
property that was passed in the POST Body:

```
{% assign request = Workflow | Attribute:'Request' | FromJSON -%}
{{ request.Body.name }}
```

Later versions may auto-decode for XML content types as well.

#### The Request properties

- `DefinedValueId` is the Id of the DefinedValue that was matched
and initiated this Workflow. This is helpful if multiple DefinedValues
exist that use the same Workflow.
- `Url` the partial path after the WebhookToWorkflow.ashx portion. If
only the WebhookToWorkflow.ashx was called without any subpath then
this will contain only `/`. This value will always begin with a `/`.
- `RawUrl` contains the full URL as requested by the client.
- `Method` is the HTTP method verb the client used, GET, POST, etc.
- `RemoteAddress` is the IP address of the remote client.
- `RemoteName` is the DNS name of the remote client, or the IP address
if it could not be resolved.
- `ServerName` The name of the server as requested by the client. For
example if the URL is `http://www.example.com/` then this will
contain the value `www.example.com` no matter what the actual local
DNS name of the server is.
- `RawBody` contains any body content supplied by the client via an
upload verb such as POST, PUT, etc.
- `Body` contains any decoded body content. If JSON data is supplied
then it will be available in this property like a normal child object.
- `Headers` will contain a dictionary of all HTTP headers supplied
by the client. This will only be available if it is enabled in the
DefinedValue. It will never contain the `Cookie` header or the
`Authorization` header.
- `Cookies` contains a dictionary of all cookies supplied by the
client. This will only be available if it is enabled in the DefinedValue.

