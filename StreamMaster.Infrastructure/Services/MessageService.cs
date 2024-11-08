﻿using MediatR;

using Microsoft.Extensions.Logging;

using StreamMaster.Application.SMMessages.Commands;

namespace StreamMaster.Infrastructure.Services
{
    public class MessageService(ILogger<MessageService> Logger, ISender sender) : IMessageService
    {
        public async Task SendError(string message, string? header = null)
        {
            Logger.LogError(message);
            SendSMErrorRequest request = new(Detail: message, Summary: header);

            await sender.Send(request);
        }
        public async Task SendError(string message, Exception? ex)
        {
            Logger.LogError(message);
            SendSMErrorRequest request = new(Detail: message, Summary: ex?.Message ?? "");

            await sender.Send(request);
        }

        public async Task SendInfo(string message, string? header = null)
        {
            Logger.LogInformation(message);
            SendSMInfoRequest request = new(Detail: message, Summary: header);

            await sender.Send(request);
        }


        public async Task SendMessage(SMMessage smMessage)
        {
            SendSMMessageRequest request = new(smMessage);
            await sender.Send(request);
        }

        public async Task SendWarn(string message)
        {
            Logger.LogWarning(message);
            SendSMWarnRequest request = new(Detail: message);

            await sender.Send(request);
        }

        public async Task SendSuccess(string message, string? header = null)
        {
            Logger.LogInformation(message);
            SendSuccessRequest request = new(Detail: message, Summary: header);

            await sender.Send(request);
        }

    }
}
