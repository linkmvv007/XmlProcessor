Настройка: appsettings.config - файлы
---

#
# Для DataProcessosService
#

##   Pаздел RabbitMq   

Cтандартные параметры подключения к Rabbit, указываются согласно вашей конфигурации RabbitMQ
```
   "HostName": "localhost",  
   "VirtualHost": "demand",   
   "Port": 5672,   
   "UserName": "admin",   
   "Password": "admin",
```
Следующие параметры отвечают за очередь, куда помещаются сообщения в случае недоступности бд или ошибок записи в бд в консьюмере.
Из указанной очереди сообщения возвращаются назад через время, указанное в параметре TtlDelay  для повторной попытки исполнения

   ```
    "XDeadLetterExchange": "retry.exchange",
    "XDeadLetterRoutingKey": "retry.key",    
    "XDeadLetterQueueName": "retry.queue",    
    "TtlDelay": 30000 
  ```


## Подключение к БД SQLite ##
```
 "Database": {
   "ConnectionString": "Data Source=modules.db"
   }
  ``` 
Имя Файла бд задается в параметре  ConnectionString. Файл бд создается автоматически , если отсутсвует на диске. 
!ничего специально устанавливать не надо

## Логирование с помощью Serilog ##
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
##   Раздел RabbitMq   ##
Стандартные параметры теже , что и в DataProcessosService
```
        "HostName": "localhost",        
         "VirtualHost": "demand",         
         "Port": 5672,       
         "UserName": "admin",         
         "Password": "admin",
  ``` 
   
Значения для  "XDeadLetterExchange" и  "XDeadLetterRoutingKey" должны быть такие же как в DataProcessosService, 
в противном случае возникнет исключение неправильной декларации очереди

  ```
   "XDeadLetterExchange": "retry.exchange",
    "XDeadLetterRoutingKey": "retry.key",
```

## Логирование с помощью Serilog ##
Реализовано с помощью serilog так же как и для DataProcessosService
