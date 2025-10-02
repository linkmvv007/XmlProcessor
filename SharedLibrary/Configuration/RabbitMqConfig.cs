namespace SharedLibrary.Configuration;

public class RabbitMqConfig
{
    public required string HostName { get; set; }
    public required string VirtualHost { get; set; }
    public int Port { get; set; } = 5672;
    public required string UserName { get; set; }
    public required string Password { get; set; }
    public required string QueueName { get; set; }


    public bool AutomaticRecoveryEnabled { get; set; } = true;
    public int NetworkRecoveryInterval { get; set; } = 10;


    public required string XDeadLetterQueueName { get; set; }
    public required string XDeadLetterExchange { get; set; }
    public required string XDeadLetterRoutingKey { get; set; }

    public int MaxRetryCount { get; set; } = 3;
    public int TtlDelay { get; set; } = 10000;
}

