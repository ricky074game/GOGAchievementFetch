using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GOGAchievementFetch
{
    public static class Program
    {
        private static readonly HttpClient HttpClient = new HttpClient();

        public static async Task Main(string[] args)
        {
            Console.WriteLine("Please enter your GOG User ID:");
            var userId = Console.ReadLine();

            Console.WriteLine("Please enter your GOG OAuth Access Token:");
            var accessToken = Console.ReadLine();

            if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(accessToken))
            {
                Console.WriteLine("User ID and Access Token cannot be empty.");
                return;
            }

            try
            {
                HttpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

                var games = await GetOwnedGamesAsync();
                if (games == null || games.Count == 0)
                {
                    Console.WriteLine("Could not retrieve any games. Please check your access token and that your profile is public.");
                    return;
                }

                var achievementsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Achievements", "GOG");
                Directory.CreateDirectory(achievementsDir);

                Console.WriteLine($"Found {games.Count} games. Fetching achievements...");

                foreach (var game in games)
                {
                    var achievementsResponse = await GetAchievementsAsync(game.Id.ToString(), userId);
                    if (achievementsResponse?.Items != null && achievementsResponse.Items.Count > 0)
                    {
                        var gameData = new GameData
                        {
                            Name = game.Title,
                            Platform = "GOG",
                            AppId = game.Id,
                            Method = "GOG API",
                            Achievements = new List<Achievement>()
                        };

                        foreach (var gogAchievement in achievementsResponse.Items)
                        {
                            gameData.Achievements.Add(new Achievement
                            {
                                Name = gogAchievement.Name,
                                Description = gogAchievement.Description,
                                ImageUrl = gogAchievement.ImageUrlUnlocked,
                                Hidden = gogAchievement.Visible ? 0 : 1,
                                ApiName = gogAchievement.AchievementKey,
                                Unlocked = gogAchievement.DateUnlocked.HasValue
                            });
                        }

                        var options = new JsonSerializerOptions { WriteIndented = true };
                        var json = JsonSerializer.Serialize(gameData, options);
                        var filePath = Path.Combine(achievementsDir, $"{game.Id}.json");
                        await File.WriteAllTextAsync(filePath, json);

                        Console.WriteLine($"Saved achievements for {game.Title} ({game.Id})");
                    }
                    else
                    {
                        Console.WriteLine($"No achievements found for {game.Title} ({game.Id}) or game does not support them.");
                    }
                }

                Console.WriteLine("\nProcessing complete.");
            }
            catch (HttpRequestException e)
            {
                Console.WriteLine($"Error fetching data from GOG API: {e.Message}");
                Console.WriteLine("Please ensure your User ID and Access Token are correct and have not expired.");
            }
            catch (Exception e)
            {
                Console.WriteLine($"An unexpected error occurred: {e.Message}");
            }
        }

        private static async Task<List<GogGame>?> GetOwnedGamesAsync()
        {
            var ownedGamesResponse = await HttpClient.GetAsync("https://embed.gog.com/user/data/games");
            ownedGamesResponse.EnsureSuccessStatusCode();
            var ownedGamesContent = await ownedGamesResponse.Content.ReadAsStringAsync();
            var ownedGamesData = JsonSerializer.Deserialize<GogOwnedGamesResponse>(ownedGamesContent);

            if (ownedGamesData?.Owned == null || ownedGamesData.Owned.Count == 0)
            {
                return new List<GogGame>();
            }

            var games = new List<GogGame>();
            Console.WriteLine($"Found {ownedGamesData.Owned.Count} owned game IDs. Fetching details...");

            foreach (var gameId in ownedGamesData.Owned)
            {
                try
                {
                    var detailsResponse = await HttpClient.GetAsync($"https://embed.gog.com/account/gameDetails/{gameId}.json");
                    detailsResponse.EnsureSuccessStatusCode();
                    var detailsContent = await detailsResponse.Content.ReadAsStringAsync();

                    if (string.IsNullOrEmpty(detailsContent))
                    {
                        Console.WriteLine($"Game details for game ID {gameId} are empty. Skipping.");
                        continue;
                    }

                    try
                    {
                        var detailsData = JsonSerializer.Deserialize<GogGameDetailsResponse>(detailsContent);

                        if (detailsData != null && !string.IsNullOrEmpty(detailsData.Title))
                        {
                            games.Add(new GogGame { Id = gameId, Title = detailsData.Title });
                            Console.WriteLine($"Fetched details for: {detailsData.Title}");
                        }
                        else
                        {
                            Console.WriteLine($"Could not deserialize game details or title is empty for game ID {gameId}. Skipping.");
                        }
                    }
                    catch (JsonException jsonEx)
                    {
                        Console.WriteLine($"Failed to parse JSON for game ID {gameId}. Error: {jsonEx.Message}. Skipping.");
                    }
                }
                catch (HttpRequestException ex)
                {
                    Console.WriteLine($"Could not fetch details for game ID {gameId}. Status: {ex.StatusCode}");
                }
            }

            return games;
        }

        private static async Task<GogAchievementsResponse?> GetAchievementsAsync(string productId, string userId)
        {
            var url = $"https://gameplay.gog.com/clients/{productId}/users/{userId}/achievements";
            string? content = null;
            try
            {
                var response = await HttpClient.GetAsync(url);
                if (!response.IsSuccessStatusCode)
                {
                    return null;
                }
                content = await response.Content.ReadAsStringAsync();
                return JsonSerializer.Deserialize<GogAchievementsResponse>(content);
            }
            catch (JsonException jsonEx)
            {
                Console.WriteLine($"Error deserializing achievements for Product ID: {productId}");
                Console.WriteLine($"Error Message: {jsonEx.Message}");
                Console.WriteLine("Raw JSON response that caused the error:");
                Console.WriteLine(content ?? "Response content was null or could not be read.");
                return null;
            }
            catch (HttpRequestException)
            {
                return null;
            }
        }
    }

    public class GogOwnedGamesResponse
    {
        [JsonPropertyName("owned")]
        public List<int>? Owned { get; set; }
    }

    public class GogGameDetailsResponse
    {
        [JsonPropertyName("title")]
        public string Title { get; } = "";
    }

    public class GogGame
    {
        public int Id { get; set; }
        public string Title { get; set; } = "";
    }

    public class GogAchievementsResponse
    {
        [JsonPropertyName("items")]
        public List<GogAchievementItem>? Items { get; set; }
    }

    public class GogAchievementItem
    {
        [JsonPropertyName("achievement_key")]
        public string AchievementKey { get; set; } = "";

        [JsonPropertyName("visible")]
        public bool Visible { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("description")]
        public string Description { get; set; } = "";

        [JsonPropertyName("image_url_unlocked")]
        public string ImageUrlUnlocked { get; set; } = "";

        [JsonPropertyName("date_unlocked")]
        public DateTime? DateUnlocked { get; set; }
    }

    public class GameData
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("platform")]
        public string Platform { get; set; } = "";

        [JsonPropertyName("imageUrl")]
        public string ImageUrl { get; set; } = "";

        [JsonPropertyName("description")]
        public string Description { get; set; } = "";

        [JsonPropertyName("method")]
        public string Method { get; set; } = "";

        [JsonPropertyName("appid")]
        public int AppId { get; set; }

        [JsonPropertyName("achievements")]
        public List<Achievement>? Achievements { get; set; }
    }

    public class Achievement
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("description")]
        public string Description { get; set; } = "";

        [JsonPropertyName("imageUrl")]
        public string ImageUrl { get; set; } = "";

        [JsonPropertyName("hidden")]
        public int Hidden { get; set; }

        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("unlocked")]
        public bool Unlocked { get; set; }

        [JsonPropertyName("apiName")]
        public string ApiName { get; set; } = "";

        [JsonPropertyName("getglobalpercentage")]
        public double GetGlobalPercentage { get; set; }

        [JsonPropertyName("difficulty")]
        public int Difficulty { get; set; }
    }
}