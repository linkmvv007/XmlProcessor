Настройка сервисов: appsettings.config - файлы
***

#
# Для DataProcessorService сервиса (консольное приложение)
#
##  Подключение к брокеру. Раздел RabbitMq
```
  "RabbitMq": {
    "HostName": "localhost",
    "VirtualHost": "demand",
    "Port": 5672,
    "UserName": "admin",
    "Password": "admin",
    "QueueName": "test_ttl_xml_processing_queue",

    "AutomaticRecoveryEnabled": true,
    "NetworkRecoveryInterval": 10,
    
    "XDeadLetterExchange": "retry.exchange",
    "XDeadLetterRoutingKey": "retry.key",
    "XDeadLetterQueueName": "retry.queue",

    "MaxRetryCount": 3,

    "TtlDelay": 30000 
  },
```

Стандартные параметры подключения к Rabbit, указываются согласно вашей конфигурации RabbitMQ
```
   "HostName": "localhost",  
   "VirtualHost": "demand",   
   "Port": 5672,   
   "UserName": "admin",   
   "Password": "admin",
```
Так же:
```
   "QueueName": "test_ttl_xml_processing_queue",
   
   "AutomaticRecoveryEnabled": true,
    "NetworkRecoveryInterval": 10,
```
**QueueName** - наименование очереди, в которую будут посылаться сообщения.   Создается автоматически с необходимыми параметрами
**AutomaticRecoveryEnabled** - автоматически восстанавливает соединение с брокеров каждый **NetworkRecoveryInterval** секунд.  

Следующие параметры отвечают за очередь, куда помещаются сообщения в случае недоступности бд или ошибок записи в бд в консьюмере.
Из указанной очереди сообщения возвращаются назад через время, указанное в параметре TtlDelay  для повторной попытки исполнения

   ```
    "XDeadLetterExchange": "retry.exchange",
    "XDeadLetterRoutingKey": "retry.key",    
    "XDeadLetterQueueName": "retry.queue",    
    "TtlDelay": 30000 
  ```
**TtlDelay** задает время в миллисекундах, указано 30 секунд  

## Подключение к БД SQLite. Раздел "Database" ##
```
 "Database": {
   "ConnectionString": "Data Source=modules.db"
   }
  ``` 
Имя Файла бд задается в **ConnectionString** в параметре **Data Source**. Файл бд создается автоматически , если отсутствует на диске.  
*!ничего специально устанавливать не надо*

## Логирование с помощью Serilog. Файл serilog.json ##
Реализовано с помощью serilog. 
Его настройки вынесены в отдельный файл serilog.json
Вывод осуществляется на консоль и в файл в папке logs с именем (указан в параметре path) log.txt. Файл формируется каждый день новый с датой в названии
```
"WriteTo": [
  { "Name": "Console" },
  {
    "Name": "File",
    "Args": {
      "path": "logs/log.txt",
      "rollingInterval": "Day"
    }
  }
],
```

#
# FileParserService
#
##  Подключение к брокеру. Раздел RabbitMq   ##
```
 "RabbitMq": {
    "HostName": "localhost", 
    "VirtualHost": "demand",
    "Port": 5672,
    "UserName": "admin",
    "Password": "admin",
    "QueueName": "test_ttl_xml_processing_queue",

    "XDeadLetterExchange": "retry.exchange",
    "XDeadLetterRoutingKey": "retry.key",

    "AutomaticRecoveryEnabled": true,
    "NetworkRecoveryInterval": 10 
  }
```
Стандартные параметры те же, что и в DataProcessorService:
```
        "HostName": "localhost",        
         "VirtualHost": "demand",         
         "Port": 5672,       
         "UserName": "admin",         
         "Password": "admin",
         "QueueName": "test_ttl_xml_processing_queue",
         "AutomaticRecoveryEnabled": true,
         "NetworkRecoveryInterval": 10 
  ``` 
   
Значения для  "XDeadLetterExchange" и "XDeadLetterRoutingKey" должны быть такие же, как в DataProcessorService, 
в противном случае возникнет исключение неправильной декларации очереди
**QueueName** значение также одинаковое с DataProcessorService
```
   "XDeadLetterExchange": "retry.exchange",
    "XDeadLetterRoutingKey": "retry.key",
```
## Логирование с помощью Serilog. Файл serilog.json ##
Реализовано с помощью serilog так же как и для DataProcessorService

## Работа с файлами, раздел XmlFiles ##
```
"XmlFiles": {
    "ErrorFolder": "_errors",
    "XmlFolder": "xml", 
    "MaxThreadsCount": 10, 
    "Ext": "*.xml" 
  }
```
 
**ErrorFolder** - Папка с невалидными xml(сюда попадают невалидные xml файлы)  
**XmlFolder** - папка, которая мониторится на наличие xml-файлов для обработки  
**MaxThreadsCount** - количество потоков, которые  задействованы для чтения xml файлов  
**Ext** - маска расширений, искомых файлов для обработки  