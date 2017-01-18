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

> Note: The workflow does not have to _finish_ for the response to be
> sent if you have provided one. Once the Workflow reaches a point where
> control is returned to the webhook, such as a Delay action, then the
> response is evaluated. This allows you to run a workflow that may take
> time to complete and send a "I'm working on that for you" message.

### Workflow Attributes (Input)

At the start of your Workflow, some attributes will be automatically
filled in if they have been defined in the Workflow.

#### Request

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

##### The Request properties

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

### Workflow Attributes (Output)

When the workflow has finished it will be checked for specific Attributes
to see if they have content relevant to the response.

#### Response

Any text in this attribute will be sent as a response to the request that
came from the client. The text is sent as is so you can either send a plain
text string or some JSON data that you have generated.

#### ContentType

If this attribute exists and is not blank then it will be used as the
Content-Type header in the response. By default this will be text/plain.


## Slack To Workflow

This provides extra parsing to deal with requests that come from Slack's
`Outgoing WebHooks` application. You can configure the default username
and icon that will be applied to all replies (if not already set by the
response from the Workflow). For an input filter you can specify the channel
and text that must match for the workflow to execute.

Both the channel and text can either be a plain string or a regular
expression (as denoted by the value beginning with `/` and ending with
`$`). If a simple string is specified for the channel then it must match
the name exactly (case insensitive). In the case of the matched text then
by default it is a case insensitive substring search.

### Workflow Attributes (Input)

The standard Attributes from the `Webhook To Workflow` module are
included.

#### Team

This will contain the "team domain" as defined in Slack. For example if
your slack domain is my_team.slack.com then this value will be `my_team`.

#### Channel

This will be populated with the name of the channel the message was sent
to. This will *not* include the `#` character. For example, a message sent
to the #general channel will cause this value to be `general`.

#### Username

The username of the person posting the message will be inserted into this
attribute. As with the channel, the `@` character will be stripped so if
the user @username sends a message, this value wil be `username`.

#### Text

This is the full text string of the message that was posted to the channel.

#### Trigger

If you defined a trigger word in Slack then this will contain the matched
trigger word from slack. This is *not* the matched text from Defined
Value.

#### ResponseUrl

If you need to to a delayed response to the user this attribute will be
populated with where to send the response via the `Slack Message` Workflow
Action.

### Workflow Attributes (Output)

The Output attributes as defined above in the `Webhook To Workflow` module
are not used with this module. The `Response` has a slightly different
syntax and the `ContentType` is not used at all.

#### Response

This will contain the message that is to be sent in response to the request
from Slack. If this is an empty string then no response is sent. If this is
a JSON parsable string then it is parsed and used as the JSON message
object. Otherwise a new JSON object is created with the text supplied as
the response message text. The response is always sent as an
`application/json` Content-Type.

If you return a JSON object and it contains a `username` property then
that will be used as the name of the user posting the reply message,
otherwise the value from the DefinedValue will be used. If that is not
defined either it will default to whatever you configured in Slack.

If you return a JSON object and it contains a `icon_url` or `icon_emoji`
property then that will be used as the icon for the message. Otherwise the
value from the DefinedValue will be used if any. If neither value has
been provided then Slack will use whatever you configured the plug-in with.

You can use the [Message Builder](https://api.slack.com/docs/messages/builder)
on Slack to play with formatting and see what options are available.
