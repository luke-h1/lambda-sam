using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.DataModel;
using Amazon.DynamoDBv2.DocumentModel;
using Amazon.DynamoDBv2.Model;
using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;
using Amazon.Lambda.SQSEvents;
using Amazon.SimpleNotificationService;
using Amazon.SimpleNotificationService.Model;
using System.Text.Json;


[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace Sam;

public class Function
{
    private readonly DynamoDBContext _dbContext;
    private readonly AmazonSimpleNotificationServiceClient _snsClient;

    private readonly string? _tasksSns;

    // ReSharper disable once PrivateFieldCanBeConvertedToLocalVariable
    private readonly AmazonDynamoDBClient _dynamoDbClient;


    public Function()
    {
        _dynamoDbClient = new AmazonDynamoDBClient();
        _dbContext = new DynamoDBContext(_dynamoDbClient);
        _snsClient = new AmazonSimpleNotificationServiceClient();
        _tasksSns = Environment.GetEnvironmentVariable("TASKS_SNS");
    }

    public async Task<APIGatewayHttpApiV2ProxyResponse> RegisterTask(APIGatewayProxyRequest input,
        ILambdaContext context)
    {
        var request = JsonSerializer.Deserialize<RegisterTaskRequest>(input.Body, new JsonSerializerOptions()
        {
            PropertyNameCaseInsensitive = true
        })!;

        var task = new TaskModel() { TaskId = Guid.NewGuid(), Name = request.Name };
        await _dbContext.SaveAsync(task);

        var @event = new PublishRequest
        {
            TopicArn = _tasksSns,
            Message = JsonSerializer.Serialize(new TaskRegistered(task.TaskId, task.Name))
        };

        await _snsClient.PublishAsync(@event);

        return new APIGatewayHttpApiV2ProxyResponse
        {
            Body = JsonSerializer.Serialize(new RegisterTaskResponse(task.TaskId)),
            StatusCode = 200,
            Headers = new Dictionary<string, string> { { "Content-Type", "application/json" } }
        };
    }

    public async Task<APIGatewayHttpApiV2ProxyResponse> GetTask(APIGatewayProxyRequest input, ILambdaContext context)
    {
        var taskId = input.PathParameters["taskId"];
        var task = await _dbContext.LoadAsync<TaskModel>(Guid.Parse(taskId));

        if (task == null)
        {
            return new APIGatewayHttpApiV2ProxyResponse
            {
                StatusCode = 404
            };
        }

        var body = JsonSerializer.Serialize(new TaskDTO(task.TaskId, task.Name));

        return new APIGatewayHttpApiV2ProxyResponse
        {
            Body = body,
            StatusCode = 200,
            Headers = new Dictionary<string, string> { { "Content-Type", "application/json" } }
        };
    }

    public async Task<APIGatewayHttpApiV2ProxyResponse> ListTasks(APIGatewayProxyRequest input, ILambdaContext context)
    {
        var name = input.QueryStringParameters["name"];
        var request = new ScanRequest()
        {
            TableName = "tasks",
            FilterExpression = "contains(#name, :name)",
            ExpressionAttributeNames = new Dictionary<string, string>()
            {
                { "#name", "name" }
            },
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>()
            {
                { ":name", new AttributeValue(name) }
            },
        };

        var response = await _dynamoDbClient.ScanAsync(request);

        var tasks = response.Items.Select(Document.FromAttributeMap)
            .Select(item => _dbContext.FromDocument<TaskModel>(item));

        var body = JsonSerializer.Serialize(tasks.Select(task => new TaskDTO(task.TaskId, task.Name)));

        return new APIGatewayHttpApiV2ProxyResponse
        {
            Body = body,
            StatusCode = 200,
            Headers = new Dictionary<string, string> { { "Content-Type", "application/json" } }
        };
    }

    public async Task<SQSBatchResponse> RegisterAssignments(SQSEvent e, ILambdaContext context)
    {
        var response = new SQSBatchResponse()
        {
            BatchItemFailures = new List<SQSBatchResponse.BatchItemFailure>()
        };

        foreach (var assignment in from record in e.Records
                 select JsonSerializer.Deserialize<SNSWrapper>(record.Body)!
                 into notification
                 select JsonSerializer.Deserialize<TaskRegistered>(notification.Messsage)
                 into task
                 select new AssignmentModel()
                     { TaskId = task.TaskId, AssignmentId = Guid.NewGuid(), Worker = Guid.NewGuid().ToString() })
        {
            await _dbContext.SaveAsync(assignment);
        }

        return response;
    }

    public async Task<APIGatewayHttpApiV2ProxyResponse> ListAssignments(APIGatewayProxyRequest input,
        ILambdaContext context)
    {
        var taskId = input.PathParameters["taskId"];
        var request = new QueryRequest()
        {
            TableName = "assignments",
            KeyConditionExpression = "taskid = :taskid",
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>()
            {
                { ":taskid", new AttributeValue(taskId) }
            }
        };

        var response = await _dynamoDbClient.QueryAsync(request);

        var assignments = response.Items.Select(Document.FromAttributeMap)
            .Select(item => _dbContext.FromDocument<AssignmentModel>(item));

        var body = JsonSerializer.Serialize(assignments.Select(assignment =>
            new AssignmentDTO(assignment.AssignmentId, assignment.TaskId, assignment.Worker)));

        return new APIGatewayHttpApiV2ProxyResponse
        {
            Body = body,
            StatusCode = 200,
            Headers = new Dictionary<string, string> { { "Content-Type", "application/json" } }
        };
    }

    [DynamoDBTable("tasks")]
    public class TaskModel
    {
        [DynamoDBHashKey("taskid")] public Guid TaskId { get; set; }

        [DynamoDBProperty("name")] public string? Name { get; set; }
    }

    [DynamoDBTable("assignments")]
    public class AssignmentModel
    {
        [DynamoDBProperty("assignmentid")] public Guid AssignmentId { get; set; }

        [DynamoDBHashKey("taskid")] public Guid TaskId { get; set; }

        [DynamoDBProperty("worker")] public string? Worker { get; set; }
    }

    public class SNSWrapper
    {
        public string Messsage { get; set; } = null!;
    }

    public record AssignmentDTO(Guid AssignmentId, Guid TaskId, string? Worker);

    public record TaskDTO(Guid TaskId, string? Name);

    public record TaskRegistered(Guid TaskId, string? Name);

    public class RegisterTaskRequest
    {
        public string? Name { get; set; }
    }

    public record RegisterTaskResponse(Guid TaskId);
}