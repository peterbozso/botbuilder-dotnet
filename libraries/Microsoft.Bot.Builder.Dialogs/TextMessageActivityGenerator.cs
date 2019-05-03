﻿using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Bot.Schema;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Microsoft.Bot.Builder.Dialogs
{
    /// <summary>
    /// The TextMessageGenerator implements IMessageActivityGenerator by using ILanguageGenerator
    /// to generate text and then uses simple markdown semantics like chatdown to create complex
    /// attachments such as herocards, image cards, image attachments etc.
    /// </summary>
    public class TextMessageActivityGenerator : IMessageActivityGenerator
    {
        // Fixed text constructor
        public TextMessageActivityGenerator(ILanguageGenerator languageGenerator)
        {
            if (languageGenerator == null)
            {
                throw new ArgumentNullException(nameof(this.LanguageGenerator));
            }

            this.LanguageGenerator = languageGenerator;
        }

        /// <summary>
        /// Gets or sets language generator.
        /// </summary>
        /// <value>
        /// LanguageGenerator to use to get text.
        /// </value>
        public ILanguageGenerator LanguageGenerator { get; set; }

        /// <summary>
        /// Generate the activity 
        /// </summary>
        /// <param name="locale">locale to generate</param>
        /// <param name="inlineTemplate">(optional) inline template definition.</param>
        /// <param name="id">id of the template to generate text.</param>
        /// <param name="data">data to bind the template to.</param>
        /// <param name="types">type hierarchy for type inheritence.</param>
        /// <param name="tags">contextual tags.</param>
        /// <returns>message activity</returns>
        public async Task<IMessageActivity> Generate(string locale, string inlineTemplate, string id, object data, string[] types, string[] tags)
        {
            var result = await this.LanguageGenerator.Generate(locale, inlineTemplate, id, data, types, tags).ConfigureAwait(false);
            return await CreateActivityFromText(result, locale, data, types, tags).ConfigureAwait(false);
        }

        /// <summary>
        /// Given a text result (text or text||speak or text||speak with [herocard... etc), create an activity
        /// </summary>
        /// <remarks>
        /// This method will create an MessageActivity from text.  It supports 3 formats
        ///     text
        ///     text || speak
        ///     text || speak [Herocard][attachment]etc...
        /// </remarks>
        /// <param name="text">text</param>
        /// <returns>MessageActivity for it</returns>
        public async Task<IMessageActivity> CreateActivityFromText(string text, string locale = null, object data = null, string[] types = null, string[] tags = null)
        {
            var activity = Activity.CreateMessageActivity();
            activity.TextFormat = TextFormatTypes.Markdown;
            var lines = text.Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

            // if result is multi line
            for (int iLine = 0; iLine < lines.Length; iLine++)
            {
                var line = lines[iLine].Trim();
                switch (line.ToLower())
                {
                    case "[herocard":
                        iLine = AddGenericCardAtttachment(activity, HeroCard.ContentType, lines, ++iLine);
                        break;

                    case "[thumbnailcard":
                        iLine = AddGenericCardAtttachment(activity, ThumbnailCard.ContentType, lines, ++iLine);
                        break;

                    case "[audiocard":
                        iLine = AddGenericCardAtttachment(activity, AudioCard.ContentType, lines, ++iLine);
                        break;

                    case "[videocard":
                        iLine = AddGenericCardAtttachment(activity, VideoCard.ContentType, lines, ++iLine);
                        break;

                    case "[animationcard":
                        iLine = AddGenericCardAtttachment(activity, AnimationCard.ContentType, lines, ++iLine);
                        break;

                    case "[signincard":
                        iLine = AddGenericCardAtttachment(activity, SigninCard.ContentType, lines, ++iLine);
                        break;

                    case "[oauthcard":
                        iLine = AddGenericCardAtttachment(activity, OAuthCard.ContentType, lines, ++iLine);
                        break;

                    case "[adaptivecard":
                        // json object
                        iLine = AddJsonAttachment(activity, lines, ++iLine);
                        break;

                    default:
                        // single line [attachment ... ] command support
                        if (line.StartsWith("[") && line.EndsWith("]"))
                        {
                            var lowerLine = line.ToLower();
                            if (lowerLine.StartsWith("[attachmentlayout="))
                            {
                                AddAttachmentLayout(activity, line);
                                break;
                            }
                            else if (lowerLine.StartsWith("[suggestions="))
                            {
                                AddSuggestions(activity, line);
                                break;
                            }
                            else if (lowerLine.StartsWith("[attachment="))
                            {
                                await AddAttachment(activity, line, locale, data, types, tags).ConfigureAwait(false);
                                break;
                            }
                        }

                        if (!String.IsNullOrEmpty(line))
                        {
                            var i = line.IndexOf("||");
                            if (i > 0)
                            {
                                activity.Text += line.Substring(0, i).Trim();
                                activity.Speak += line.Substring(i + 2).Trim();
                            }
                            else
                            {
                                activity.Text += line.Trim();
                                activity.Speak += line.Trim();
                            }

                            if (iLine != (lines.Length - 1))
                            {
                                activity.Text += "\n";
                                activity.Speak += "\n";
                            }
                        }
                        break;
                }
            }

            return activity;
        }

        private async Task AddAttachment(IMessageActivity activity, string line, string locale, object data, string[] types, string[] tags)
        {
            var parts = line.Split('=');
            if (parts.Length == 1)
            {
                throw new ArgumentOutOfRangeException($"Missing = seperator in {line}");
            }

            var value = parts[1].TrimEnd(']').Trim();
            var parts2 = value.Split(' ');
            var contentUrl = parts2[0];
            var attachment = new Attachment(contentUrl: contentUrl);

            if (parts2.Length == 2)
            {
                switch (parts2[1].ToLower())
                {
                    case "animation":
                        attachment.ContentType = AnimationCard.ContentType;
                        attachment.Content = await readAttachmentFile(contentUrl, attachment.ContentType, isCard: true, locale: locale, data: data, types: types, tags: tags).ConfigureAwait(false);
                        break;
                    case "audio":
                        attachment.ContentType = AudioCard.ContentType;
                        attachment.Content = await readAttachmentFile(contentUrl, attachment.ContentType, isCard: true, locale: locale, data: data, types: types, tags: tags).ConfigureAwait(false);
                        break;
                    case "hero":
                        attachment.ContentType = HeroCard.ContentType;
                        attachment.Content = await readAttachmentFile(contentUrl, attachment.ContentType, isCard: true, locale: locale, data: data, types: types, tags: tags).ConfigureAwait(false);
                        break;
                    case "receipt":
                        attachment.ContentType = ReceiptCard.ContentType;
                        attachment.Content = await readAttachmentFile(contentUrl, attachment.ContentType, isCard: true, locale: locale, data: data, types: types, tags: tags).ConfigureAwait(false);
                        break;
                    case "thumbnail":
                        attachment.ContentType = ThumbnailCard.ContentType;
                        attachment.Content = await readAttachmentFile(contentUrl, attachment.ContentType, isCard: true, locale: locale, data: data, types: types, tags: tags).ConfigureAwait(false);
                        break;
                    case "signin":
                        attachment.ContentType = SigninCard.ContentType;
                        attachment.Content = await readAttachmentFile(contentUrl, attachment.ContentType, isCard: true, locale: locale, data: data, types: types, tags: tags).ConfigureAwait(false);
                        break;
                    case "video":
                        attachment.ContentType = VideoCard.ContentType;
                        attachment.Content = await readAttachmentFile(contentUrl, attachment.ContentType, isCard: true, locale: locale, data: data, types: types, tags: tags).ConfigureAwait(false);
                        break;
                    case "adaptivecard":
                        attachment.ContentType = "application/vnd.microsoft.card.adaptive";
                        attachment.Content = await readAttachmentFile(contentUrl, attachment.ContentType, isCard: true, locale: locale, data: data, types: types, tags: tags).ConfigureAwait(false);
                        break;
                    default:
                        attachment.ContentType = parts2[1].Trim();
                        attachment.Content = await readAttachmentFile(contentUrl, attachment.ContentType, isCard: false, locale: locale, data: data, types: types, tags: tags).ConfigureAwait(false);
                        break;
                }
            }

            if (attachment.Content != null && attachment.Content is string && ((string)attachment.Content).StartsWith("data:"))
            {
                attachment.ContentUrl = (string)attachment.Content;
                attachment.Content = null;
            }

            if (attachment.Content != null)
            {
                // if we are sending content, then no need for contentUrl
                attachment.ContentUrl = null;
            }

            activity.Attachments.Add(attachment);
        }

        protected async Task<object> readAttachmentFile(string fileLocation, string contentType, bool isCard, string locale, object data, string[] types, string[] tags)
        {
            if (Uri.TryCreate(fileLocation, UriKind.Absolute, out Uri uri))
            {
                return null;
            }

            var resolvedFileLocation = Path.Combine(Environment.CurrentDirectory, fileLocation);
            var exists = File.Exists(resolvedFileLocation);

            // fallback to cwd
            if (!exists)
            {
                resolvedFileLocation = fileLocation;
            }

            // Throws if the fallback does not exist.
            if (contentType.ToLower().IndexOf("json") > 0 || isCard)
            {
                var inlineTemplate = $"```\n{File.ReadAllText(resolvedFileLocation)}\n```";
                var result = await this.LanguageGenerator.Generate(locale, inlineTemplate, null, data, types, tags).ConfigureAwait(false);
                if (result != null)
                {
                    return JsonConvert.DeserializeObject(result);
                }
                throw new Exception("No template found!");
            }
            else
            {
                return $"data:{contentType}; base64, {Convert.ToBase64String(File.ReadAllBytes(resolvedFileLocation))}";
            }
        }


        private static int AddJsonAttachment(IMessageActivity activity, string[] lines, int iLine)
        {
            StringBuilder sb = new StringBuilder();
            for (; iLine < lines.Length; iLine++)
            {
                if (lines[iLine].TrimEnd() == "]")
                {
                    break;
                }

                sb.AppendLine(lines[iLine]);
            }

            dynamic obj = JsonConvert.DeserializeObject(sb.ToString());
            string contentType = "application/json";

            if (obj.type == "AdaptiveCard")
            {
                contentType = "application/vnd.microsoft.card.adaptive";
            }

            var attachment = new Attachment(contentType, content: obj);
            activity.Attachments.Add(attachment);
            return iLine;
        }

        private static void AddSuggestions(IMessageActivity activity, string line)
        {
            var value = line.Split('=');
            if (value.Length > 1)
            {
                var suggestions = value[1].Split('|');
                activity.SuggestedActions = new SuggestedActions();
                activity.SuggestedActions.Actions = suggestions.Select(s =>
                {
                    var text = s.TrimEnd(']').Trim();
                    return new CardAction(type: ActionTypes.MessageBack, title: text, displayText: text, text: text);
                }).ToList();
            }
        }

        private static void AddAttachmentLayout(IMessageActivity activity, string line)
        {
            var value = line.Split('=');
            if (value.Length > 1)
            {
                activity.AttachmentLayout = value[1].TrimEnd(']').Trim();
            }
        }

        private static int AddGenericCardAtttachment(IMessageActivity activity, string type, string[] lines, int iLine)
        {
            var attachment = new Attachment(type, content: new JObject());
            iLine = BuildGenericCard(attachment.Content, type, lines, iLine);
            activity.Attachments.Add(attachment);
            return iLine;
        }

        private static int BuildGenericCard(dynamic card, string type, string[] lines, int iLine)
        {
            bool lastLine = false;

            for (; !lastLine && iLine < lines.Length; iLine++)
            {
                var line = lines[iLine];
                var start = line.IndexOf('=');
                if (start > 0)
                {
                    var property = line.Substring(0, start).Trim().ToLower();
                    var value = line.Substring(start + 1).Trim();
                    if (value.EndsWith("]"))
                    {
                        value = value.TrimEnd(']');
                        lastLine = true;
                    }

                    switch (property.ToLower())
                    {
                        case "title":
                        case "subtitle":
                        case "text":
                        case "aspect":
                        case "value":
                        case "connectionName":
                            card[property] = value;
                            break;

                        case "image":
                        case "images":
                            if (type == HeroCard.ContentType || type == ThumbnailCard.ContentType)
                            {
                                // then it's images
                                if (card["images"] == null)
                                {
                                    card["images"] = new JArray();
                                }

                                var urlObj = new JObject() { { "url", value } };
                                ((JArray)card["images"]).Add(urlObj);
                            }
                            else
                            {
                                // then it's image
                                var urlObj = new JObject() { { "url", value } };
                                card["image"] = urlObj;
                            }
                            break;

                        case "media":
                            if (card[property] == null)
                            {
                                card[property] = new JArray();
                            }

                            var mediaObj = new JObject() { { "url", value } };
                            ((JArray)card[property]).Add(mediaObj);
                            break;

                        case "buttons":
                            if (card[property] == null)
                            {
                                card[property] = new JArray();
                            }

                            foreach (var button in value.Split('|'))
                            {
                                var buttonObj = new JObject() { { "title", button.Trim() }, { "type", "imBack" }, { "value", button.Trim() } };
                                ((JArray)card[property]).Add(buttonObj);
                            }
                            break;

                        case "autostart":
                        case "sharable":
                        case "autoloop":
                            card[property] = value.ToLower() == "true";
                            break;
                        case "":
                            break;
                        default:
                            System.Diagnostics.Debug.WriteLine(string.Format("Skipping unknown card property {0}", property));
                            break;
                    }

                    if (lastLine)
                    {
                        break;
                    }
                }
            }

            return iLine;
        }

    }
}