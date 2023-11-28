using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.DataModel;
using Amazon.DynamoDBv2.DocumentModel;
using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;
using GetAllChats.Models;
using System.Net;
using System.Text.Json;

// Assembly attribute to enable the Lambda function's JSON input to be converted into a .NET class.
[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace GetAllChats;

public class Function
{
    private readonly AmazonDynamoDBClient _client;
    private readonly DynamoDBContext _context;

    public Function()
    {
        _client = new AmazonDynamoDBClient();
        _context = new DynamoDBContext(_client);
    }

    public async Task<APIGatewayProxyResponse> FunctionHandler(APIGatewayProxyRequest request, ILambdaContext context)
    {
		// Отримаемо ідентифікатор користувача з запиту
        var userId = request.QueryStringParameters["userId"];

		// Отримаемо список чатів для даного користувача
        List<Chat> chats = await GetAllChats(userId);

 		// Створюємо список для результатів
        var result = new List<GetAllChatsResponseItem>(chats.Count);

		// Логіка пагінації
		request.QueryStringParameters.TryGetValue("pageSize", out var pageSizeString);
        int.TryParse(pageSizeString, out var pageSize);
        pageSize = pageSize == 0 ? 50 : pageSize;

		if (pageSize > 1000 || pageSize < 1)
        {
            return new APIGatewayProxyResponse()
            {
                StatusCode = (int)HttpStatusCode.OK,
                Headers = new Dictionary<string, string>
                {
                    { "Content-Type", "application/json" },
                    { "Access-Control-Allow-Origin", "*" }
                },

                Body = "Invalid pageSize."
            };
        }

		// Проходимось по кожному чату
		foreach (var chat in chats)
		{
			// Створюємо об'єкт GetAllChatsResponseItem на основі даних чату та його учасників
			var responseItem = new GetAllChatsResponseItem
			{
				ChatId = chat.ChatId,
				UpdateDt = chat.UpdateDt,
				User1 = chat.User1,
				User2 = chat.User2
			};

			// Додаємо результат до списку результатів
        	result.Add(responseItem);
		}

		request.QueryStringParameters.TryGetValue("lastid", out var lastId);

		// Виділяємо необхідну кількість результатів для пагінації
		var paginatedResults = string.IsNullOrEmpty(lastId) 
			? result.Take(pageSize).ToList() 
			: result.SkipWhile(r => r.ChatId != lastId).Skip(1).Take(pageSize).ToList();


		// Створюємо відповідь для API Gateway
        return new APIGatewayProxyResponse
        {
            StatusCode = (int)HttpStatusCode.OK,
            Headers = new Dictionary<string, string>
            {
                { "Content-Type", "application/json" },
                { "Access-Control-Allow-Origin", "*" }
            },

            Body = JsonSerializer.Serialize(new
			{
				PaginationToken = lastId, 
				Chats = paginatedResults
			})
        };
    }

    private async Task<List<Chat>> GetAllChats(string userId)
    {
        var user1 = new QueryOperationConfig()
        {
            IndexName = "user1-updatedDt-index",
            KeyExpression = new Expression()
            {
                ExpressionStatement = "user1 = :user",
                ExpressionAttributeValues = new Dictionary<string, DynamoDBEntry>() { { ":user", userId } }
            }
        };
        var user1Results = await _context.FromQueryAsync<Chat>(user1).GetRemainingAsync();

        var user2 = new QueryOperationConfig()
        {
            IndexName = "user2-updatedDt-index",
            KeyExpression = new Expression()
            {
                ExpressionStatement = "user2 = :user",
                ExpressionAttributeValues = new Dictionary<string, DynamoDBEntry>() { { ":user", userId } }
            }
        };
        var user2Results = await _context.FromQueryAsync<Chat>(user2).GetRemainingAsync();

        user1Results.AddRange(user2Results);
        return user1Results.OrderBy(x => x.UpdateDt).ToList();
    }
}
