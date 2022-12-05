using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SendGrid.Helpers.Mail;

using EastFive.Web.Services;
using EastFive.Collections.Generic;
using EastFive.Linq;
using EastFive.Api;
using EastFive.Web.Configuration;

namespace EastFive.Communications.Azure
{
    public class SendGridMailer : ISendMessageService
    {
        private string apiKey;

        public SendGridMailer(string apiKey)
        {
            this.apiKey = apiKey;
        }

        public static TResult Load<TResult>(
            Func<SendGridMailer,TResult> onSuccess,
            Func<string,TResult> onFailure)
        {
            return EastFive.Azure.AppSettings.Communications.SendGrid.ApiKey.ConfigurationString(
                key => onSuccess(new SendGridMailer(key)),
                onFailure);
        }

        public async Task<SendMessageTemplate[]> ListTemplatesAsync()
        {
            var client = new global::SendGrid.SendGridClient(apiKey);
            var responseTemplates = await client.RequestAsync(global::SendGrid.SendGridClient.Method.GET, urlPath: $"/templates");
            var templateInfo = await responseTemplates.Body.ReadAsStringAsync();
            var converter = new Newtonsoft.Json.Converters.ExpandoObjectConverter();
            dynamic obj = Newtonsoft.Json.JsonConvert.DeserializeObject<System.Dynamic.ExpandoObject>(templateInfo, converter);
            return ((List<object>)obj.templates).Select((dynamic tmp) => new SendMessageTemplate()
            {
                externalTemplateId = tmp.id,
                name = tmp.name,
            }).ToArray();
        }

        public async Task<TResult> SendEmailMessageAsync<TResult>(
            string templateName,
            string toAddress, string toName,
            string fromAddress, string fromName,
            string subject,
            IDictionary<string, string> substitutionsSingle,
            IDictionary<string, IDictionary<string, string>[]> substitutionsMultiple,
            Func<string, TResult> onSuccess, 
            Func<TResult> onServiceUnavailable,
            Func<string, TResult> onFailure)
        {
            var message = new SendGridMessage();
            message.From = new EmailAddress(fromAddress, fromName);
            message.Subject = subject;
            message.SetClickTracking(false, true);
            //message.TemplateId = templateName;

            var (emailMute, toAddressEmail) = EastFive.Azure.AppSettings.Communications.MuteEmailToAddress.ConfigurationString(
                (emailMuteString) =>
                {
                    if (emailMuteString.IsNullOrWhiteSpace())
                        return (false, new EmailAddress(toAddress, toName));
                    
                    var muteAddr = new EmailAddress(emailMuteString, $"MUTED[{toAddress}:{toName}]");
                    return (true, muteAddr);
                },
                (why) => (false, new EmailAddress(toAddress, toName)));

            message.AddTo(toAddressEmail);
            // message.SetClickTracking(false, false);

            var bccAddressesAdded = EastFive.Azure.AppSettings.Communications.BccAllAddresses.ConfigurationString(
                copyEmail =>
                {
                    var bccAddresses = (copyEmail.IsNullOrWhiteSpace() ? "" : copyEmail)
                        .Split(',')
                        .Where(s => !String.IsNullOrWhiteSpace(s))
                        .Select((bccAddress) => new EmailAddress(bccAddress))
                        .ToList();
                    if (bccAddresses.Any())
                        message.AddBccs(bccAddresses);
                    return true;
                },
                (why) => false);

            
            var subsitutionsSingleDictionary = substitutionsSingle
                .Select(kvp => new KeyValuePair<string, string>($"--{kvp.Key}--", kvp.Value))
                .ToDictionary();
            message.AddSubstitutions(subsitutionsSingleDictionary);
            var client = new global::SendGrid.SendGridClient(apiKey);

            var responseTemplates = await client.RequestAsync(global::SendGrid.SendGridClient.Method.GET, urlPath: $"/templates/{templateName}");
            if (responseTemplates.StatusCode == System.Net.HttpStatusCode.NotFound)
                return onFailure($"The specified template [{templateName}] does not exist.");
            var templateInfo = await responseTemplates.Body.ReadAsStringAsync();
            if (!responseTemplates.StatusCode.IsSuccess())
                return onFailure($"Failed to aquire template:{templateInfo}");

            var converter = new Newtonsoft.Json.Converters.ExpandoObjectConverter();
            dynamic obj = Newtonsoft.Json.JsonConvert.DeserializeObject<System.Dynamic.ExpandoObject>(templateInfo, converter);
            string html = obj.versions[0].html_content;
            var htmlDoc = new HtmlAgilityPack.HtmlDocument();
            htmlDoc.LoadHtml(html);
            if (htmlDoc.ParseErrors != null && htmlDoc.ParseErrors.Count() > 0)
                return onFailure($"Template has parse errors:{htmlDoc.ParseErrors.Select(pe => pe.Reason).Join(";")}");

            var substitutionsMultipleExpanded = substitutionsMultiple
                .NullToEmpty()
                .SelectMany(
                    (substitutionMultiple) =>
                    {
                        var matchingNodes = htmlDoc.DocumentNode.SelectNodes($"//*[@data-repeat='--{substitutionMultiple.Key}--']");
                        if (!matchingNodes.NullToEmpty().Any())
                            return new HtmlAgilityPack.HtmlNode[] { };

                        var substituations = matchingNodes
                                .Select(
                                    matchingNode =>
                                    {
                                        var parentNode = substitutionMultiple.Value
                                            .Where(
                                                subValues =>
                                                {
                                                    if (!matchingNode.Attributes.Contains("data-repeat-selector-key"))
                                                        return true;
                                                    if (!matchingNode.Attributes.Contains("data-repeat-selector-value"))
                                                        return true;
                                                    var key = matchingNode.Attributes["data-repeat-selector-key"].Value;
                                                    if (!subValues.ContainsKey(key))
                                                        return false;
                                                    var value = matchingNode.Attributes["data-repeat-selector-value"].Value;
                                                    return subValues[key] == value;
                                                })
                                            .Aggregate(matchingNode.ParentNode,
                                                (parentNodeAggr, subValues) =>
                                                {
                                                    var newChildHtml =  subValues
                                                        .Aggregate(
                                                            matchingNode.OuterHtml,
                                                            (subTextAggr, sub) =>
                                                            {
                                                                subTextAggr = subTextAggr.Replace($"--{sub.Key}--", sub.Value);
                                                                return subTextAggr;
                                                            });

                                                    var childNode = HtmlAgilityPack.HtmlNode.CreateNode(newChildHtml);
                                                    parentNodeAggr.AppendChild(childNode);
                                                    return parentNodeAggr;
                                                });

                                        parentNode.RemoveChild(matchingNode);
                                        //return new KeyValuePair<string, string>(matchingNode.OuterHtml, subText);
                                        return matchingNode;
                                    })
                                .ToArray();
                        return substituations;
                    })
                .ToArray();

            // message.AddSubstitutions(substitutionsMultipleExpanded);
            //message.HtmlContent = htmlDoc.DocumentNode.OuterHtml;
            message.PlainTextContent = htmlDoc.DocumentNode.InnerText;

            message.HtmlContent = subsitutionsSingleDictionary.Aggregate(
                htmlDoc.DocumentNode.OuterHtml,
                (outerHtml, substitutionSingle) =>
                {
                    return outerHtml.Replace($"--{substitutionSingle.Key}--", substitutionSingle.Value);
                });

            // Send the email, which returns an awaitable task.
            try
            {
                var response = await client.SendEmailAsync(message);
                var body = await response.Body.ReadAsStringAsync();
                if (!response.StatusCode.IsSuccess())
                    return onFailure(body);

                var messageIdHeaders = response.Headers
                    .Where(header => header.Key == "X-Message-Id");
                if(!messageIdHeaders.Any())
                    return onSuccess(body);

                var messageIds = messageIdHeaders.First().Value;
                if (!messageIds.Any())
                    return onSuccess(body);

                var messageId = messageIds.First();
                return onSuccess(messageId);
            }
            catch (Exception ex)
            {
                //var details = new StringBuilder();

                //details.Append("ResponseStatusCode: " + ex.ResponseStatusCode + ".   ");
                //for (int i = 0; i < ex.Errors.Count(); i++)
                //{
                //    details.Append(" -- Error #" + i.ToString() + " : " + ex.Errors[i]);
                //}

                return onFailure(ex.ToString());
            }
        }
    }
}
