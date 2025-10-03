using RabbitMQ.Client.Events;

namespace FileParserService;

public class RabbitMqReturnException : ApplicationException
{
    public BasicReturnEventArgs ReturnArgs { get; }
    public RabbitMqReturnException(BasicReturnEventArgs args, string message)
        : base( message)
    {
        ReturnArgs = args;
    }
}