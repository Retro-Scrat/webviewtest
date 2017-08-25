#r "Newtonsoft.Json"
#load "EchoDialog.csx"

using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using Newtonsoft.Json;

using Microsoft.Bot.Builder.Azure;
using Microsoft.Bot.Builder.Dialogs;
using Microsoft.Bot.Connector;

public static async Task<object> Run(HttpRequestMessage req, TraceWriter log)
{

    log.Info($"Webhook was triggered!");

    // Initialize the azure bot
    using (BotService.Initialize())
    {
        // Deserialize the incoming activity
        string jsonContent = await req.Content.ReadAsStringAsync();
        var activity = JsonConvert.DeserializeObject<Activity>(jsonContent);

        // authenticate incoming request and add activity.ServiceUrl to MicrosoftAppCredentials.TrustedHostNames
        // if request is authenticated
        if (!await BotService.Authenticator.TryAuthenticateAsync(req, new[] { activity }, CancellationToken.None))
        {
            return BotAuthenticator.GenerateUnauthorizedResponse(req);
        }

        if (activity != null)
        {
            // one of these will have an interface and process it
            switch (activity.GetActivityType())
            {
                case ActivityTypes.Message:
                    if (activity.Type == ActivityTypes.Message)
                    {
                        var attachments = activity.Attachments;
                        if (activity.Text == "--webview")
                        {
                            using (ConnectorClient connector = new ConnectorClient(new Uri(activity.ServiceUrl)))
                            {
                                Activity reply = activity.CreateReply();
                                // reply.ChannelData = new
                                // {
                                //     attachment = new
                                //     {
                                //         type = "template",
                                //         payload = new
                                //         {
                                //             template_type = "button",
                                //             text = "I can haz cheeseburger?",
                                //             buttons = new object[]{
                                //                 new
                                //                 {
                                //                     type = "web_url",
                                //                     url = "https://www.google.com",
                                //                     title = "Visit Google",
                                //                     webview_height_ratio = "compact",
                                //                 }
                                //             }
                                //         }
                                //     }
                                // };
                                // reply.Type = ActivityTypes.Message;HeroCard card = new HeroCard();
                                // card.Text = $"You are {activity.From.Name}";
                                // card.Tap = null;

                                // card.Buttons.Add(
                                //     new CardAction(
                                //         "openUrl",
                                //         title: "I can haz cheeseburger?",
                                //         value: "https://webcamtoy.com/"));

                                // reply.Attachments.Add(card.ToAttachment());
                                reply.Text = $"{activity.From.Id}";

                                await connector.Conversations.SendToConversationAsync(reply);
                            }
                        }
                        else if (attachments != null && attachments.Any())
                        {
                            try
                            {
                                Attachment attachment = null;
                                foreach (var a in attachments)
                                {

                                    attachment = a;
                                }
                                //The message sent does not contain an image
                                if (attachment == null)
                                {
                                    //TODO : Tell feersum that an image was not sent
                                    await sendTextToUser(activity, "Please upload an image of your drivers licence (no image in message)");
                                    return "";
                                }
                                byte[] img = await getAttachment(attachment);

                                var res = await decodeImage(img, attachment.Name != null ? attachment.Name : $"unnamedImage_{DateTime.UtcNow.Ticks}.jpg");

                                if (res.IsSuccessStatusCode)
                                {
                                    string content = await res.Content.ReadAsStringAsync();
                                    Console.WriteLine(content);

                                    await sendTextToUser(activity, content);

                                    activity.Text = content;
                                }
                                else
                                {
                                    string text = await res.Content.ReadAsStringAsync();
                                    await sendTextToUser(activity, $"Failed To Decode Image - Please upload a new Image\n{res.StatusCode}");
                                }
                            }
                            catch (Exception ex)
                            {
                                // Exception
                            }
                        }
                        else
                        {
                            await Conversation.SendAsync(activity, () => new EchoDialog());
                        }
                    }
                    break;
                case ActivityTypes.ConversationUpdate:
                    var client = new ConnectorClient(new Uri(activity.ServiceUrl));
                    IConversationUpdateActivity update = activity;
                    if (update.MembersAdded.Any())
                    {
                        var reply = activity.CreateReply();
                        var newMembers = update.MembersAdded?.Where(t => t.Id != activity.Recipient.Id);
                        foreach (var newMember in newMembers)
                        {
                            reply.Text = "Welcome";
                            if (!string.IsNullOrEmpty(newMember.Name))
                            {
                                reply.Text += $" {newMember.Name}";
                            }
                            reply.Text += "!";
                            await client.Conversations.ReplyToActivityAsync(reply);
                        }
                    }
                    break;
                case ActivityTypes.ContactRelationUpdate:
                case ActivityTypes.Typing:
                case ActivityTypes.DeleteUserData:
                case ActivityTypes.Ping:
                default:
                    log.Error($"Unknown activity type ignored: {activity.GetActivityType()}");
                    break;
            }
        }

    }
    return req.CreateResponse(HttpStatusCode.Accepted);
}



bool IsImageMIME(string mime)
{
    return mime.Contains("image");
}

static async Task<byte[]> getAttachment(Attachment attachment)
{
    HttpClient client = new HttpClient();
    client.BaseAddress = new Uri(attachment.ContentUrl);
    byte[] bytes = await client.GetByteArrayAsync("");

    return bytes;
}

static async Task<HttpResponseMessage> decodeImage(byte[] img, string name = "data")
{

    string backendUrl = "http://stistaging.westeurope.cloudapp.azure.com:8386/api/v1/";
    HttpClient client = new HttpClient();
    client.BaseAddress = new Uri(backendUrl + "imageDecoding/driversLicense/decode");//baseUrl);
    MultipartFormDataContent form = new MultipartFormDataContent();
    form.Add(new ByteArrayContent(img, 0, img.Length), "ByteArray", name);
    HttpResponseMessage res = await client.PostAsync("", form);

    return res;
}

static async Task sendTextToUser(Activity activity, string text)
{
    using (ConnectorClient connector = new ConnectorClient(new Uri(activity.ServiceUrl)))
    {
        Activity reply = activity.CreateReply();
        reply.Type = ActivityTypes.Message;
        reply.Text = text;
        await connector.Conversations.SendToConversationAsync(reply);
    }
}
