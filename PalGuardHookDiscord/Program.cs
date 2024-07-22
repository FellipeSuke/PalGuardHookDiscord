using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using static Program;

class Program
{

    private static readonly ConcurrentDictionary<string, long> filePositions = new ConcurrentDictionary<string, long>();
    private static string currentLogFile = null;
    static string discordWebhookUrl_Logs;
    static string discordWebhookUrl_Cheater;
    static string discordWebhookUrl_AtaquesGuild;

    static string GuildExportFilePath = @"\\OPTSUKE01\steamcmd\steamapps\common\PalServer\Pal\Binaries\Win64\palguard\guildexport.json";
    static string PlayersApiUrl = "http://192.168.100.73:8212/v1/api/players";
    static string ApiAuthorizationHeader = "Basic YWRtaW46dW5yZWFs";

    static async Task Main(string[] args)
    {
        // Obtém o diretório de logs a partir de variáveis de ambiente ou usa um caminho padrão
        string logDirectory = Environment.GetEnvironmentVariable("LOG_DIRECTORY") ?? @"\\192.168.100.73\palguard\logs";
        string dataDirectory = Environment.GetEnvironmentVariable("DATA_DIRECTORY") ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Dados");
        discordWebhookUrl_Logs = Environment.GetEnvironmentVariable("DISCORD_WEBHOOK_URL_Logs") ?? "https://discord.com/api/webhooks/1264730050935263273/dFORkiYBVRu0muxAp8V6Kvj9_nmTaCjn_I1SXH_FrXmhdm1ZiEKE1MMIL5Xd3DhiNOAe";
        discordWebhookUrl_Cheater = Environment.GetEnvironmentVariable("DISCORD_WEBHOOK_URL_Cheater") ?? "https://discord.com/api/webhooks/1264728493967671446/Bp-gXNAH__HISjDMeSkYyIane-iQ38HE9QhIkXhPqq8DrTXErfGQThhA6YZoy3EZgu3a";
        discordWebhookUrl_AtaquesGuild = Environment.GetEnvironmentVariable("DISCORD_WEBHOOK_URL_AtaquesGuild") ?? "https://discord.com/api/webhooks/1264728721835954196/ZZRHjttkYbN-WL1EkgWntgdZtLuuijeFFafSY0xXytbhukvgJpRmuGpz2qKPEY5QgmwS";

        // Cria o diretório de dados se ele não existir
        if (!Directory.Exists(dataDirectory))
        {
            Directory.CreateDirectory(dataDirectory);
        }

        Console.WriteLine("Monitorando o diretório: " + logDirectory);

        // Obtém o arquivo de log mais recente
        currentLogFile = GetLatestLogFile(logDirectory);
        if (currentLogFile != null)
        {
            Console.WriteLine("Arquivo de log mais recente: " + currentLogFile);
            _ = Task.Run(() => MonitorLogFile(currentLogFile, true));
        }
        else
        {
            Console.WriteLine("Nenhum arquivo de log encontrado no diretório.");
        }

        // Configura o FileSystemWatcher para monitorar mudanças no diretório de logs
        FileSystemWatcher watcher = new FileSystemWatcher
        {
            Path = logDirectory,
            Filter = "*.txt",
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite
        };

        watcher.Created += OnChanged;
        watcher.Changed += OnChanged;

        watcher.EnableRaisingEvents = true;

        Console.WriteLine("Pressione 'q' para sair.");
        while (Console.Read() != 'q') ;

        await Task.Delay(1000);
    }

    // Evento acionado quando um arquivo no diretório de logs é criado ou modificado
    private static void OnChanged(object source, FileSystemEventArgs e)
    {
        Console.WriteLine($"Arquivo: {e.FullPath} {e.ChangeType}");
        if (e.ChangeType == WatcherChangeTypes.Created)
        {
            currentLogFile = e.FullPath;
            _ = Task.Run(() => MonitorLogFile(e.FullPath, true));
        }
    }

    // Obtém o arquivo de log mais recente no diretório
    private static string GetLatestLogFile(string directory)
    {
        var directoryInfo = new DirectoryInfo(directory);
        var file = directoryInfo.GetFiles("*.txt")
                               .OrderByDescending(f => f.LastWriteTime)
                               .FirstOrDefault();
        return file?.FullName;
    }

    // Monitora o arquivo de log em busca de novas linhas
    private static async Task MonitorLogFile(string filePath, bool isFirstRead)
    {
        try
        {
            while (true)
            {
                await ReadLogFile(filePath, isFirstRead);
                isFirstRead = false;
                await Task.Delay(1000);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Erro ao monitorar o arquivo: {ex.Message}");
        }
    }

    // Lê o arquivo de log a partir da posição onde foi lido pela última vez
    private static async Task ReadLogFile(string filePath, bool isFirstRead)
    {
        try
        {
            using (FileStream fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (StreamReader reader = new StreamReader(fs))
            {
                fs.Seek(filePositions.GetOrAdd(filePath, 0), SeekOrigin.Begin);

                string logLine;
                string lastLine = null;
                while ((logLine = await reader.ReadLineAsync()) != null)
                {
                    lastLine = logLine;
                }

                if (isFirstRead && lastLine != null)
                {
                    Console.WriteLine(lastLine);
                    ProcessLogLine(lastLine);
                    filePositions[filePath] = fs.Position;
                }
                else
                {
                    fs.Seek(filePositions.GetOrAdd(filePath, 0), SeekOrigin.Begin);
                    while ((logLine = await reader.ReadLineAsync()) != null)
                    {
                        Console.WriteLine(logLine);
                        ProcessLogLine(logLine);
                    }
                    filePositions[filePath] = fs.Position;
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Erro ao ler o arquivo: {ex.Message}");
        }
    }

    // Processa cada linha do log
    private static async Task ProcessLogLine(string logLine)
    {
        string formattedMessage = null;

        if (logLine.Contains("have dealt"))
        {
            formattedMessage = await FormatAttackMessage(logLine);
            if (formattedMessage != null)
            {
                await SendDiscordNotification(formattedMessage, discordWebhookUrl_AtaquesGuild);
            }
        }
        else if (logLine.Contains("is a cheater"))
        {
            formattedMessage = logLine;
            await SendDiscordNotification(formattedMessage, discordWebhookUrl_Cheater);
        }

        // Envia a mensagem original para o webhook de logs
        await SendDiscordNotification(logLine, discordWebhookUrl_Logs);
    }

    // Formata a mensagem de ataque
    private static async Task<string> FormatAttackMessage(string logLine)
    {
        // Carrega os dados das guildas do arquivo JSON
        var guildData = LoadGuildData(GuildExportFilePath);
        // Obtém a lista de jogadores online
        var onlinePlayers = await GetOnlinePlayers();

        // Extrai as informações da linha do log
        string time = logLine.Substring(1, 8);
        string attacker = ExtractAttackerName(logLine);
        string guildName = ExtractGuild(logLine);

        // Itera sobre as guildas carregadas
        foreach (var guildEntry in guildData.Guilds)
        {
            // Verifica se o nome da guilda corresponde ao nome extraído do log
            if (guildEntry.Value.Name == guildName)
            {
                // Filtra os membros online da guilda
                var onlineMembers = new List<string>();
                foreach (var member in guildEntry.Value.Members)
                {
                    if (onlinePlayers.Contains(member.Value.NickName))
                    {
                        onlineMembers.Add(member.Value.NickName);
                    }
                }

                // Cria uma lista formatada com os membros online
                var onlineMembersList = onlineMembers.Any() ? string.Join(", ", onlineMembers) : "Nenhum membro online";

                // Retorna a mensagem formatada
                return $"{time} {attacker} atacou a guilda {guildEntry.Value.Name} ({onlineMembersList})";
            }
        }

        // Retorna null se a guilda não for encontrada
        return null;
    }


    private static GuildContext LoadGuildData(string filePath)
    {
        var guildDataJson = File.ReadAllText(filePath);
        var guilds = JsonConvert.DeserializeObject<Dictionary<string, Guild>>(guildDataJson);
        return new GuildContext { Guilds = guilds };
    }
    public class GuildContext
    {
        public Dictionary<string, Guild> Guilds { get; set; }
    }

    private static async Task<List<string>> GetOnlinePlayers()
    {
        var client = new HttpClient();
        var request = new HttpRequestMessage(HttpMethod.Get, PlayersApiUrl);
        request.Headers.Add("Authorization", ApiAuthorizationHeader);
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var responseBody = await response.Content.ReadAsStringAsync();

        var apiResponse = JsonConvert.DeserializeObject<ApiResponse>(responseBody);
        return apiResponse.Players.Select(p => p.Name).ToList();
    }

    // Extrai o nome do atacante da linha de log
    private static string ExtractAttackerName(string logLine)
    {
        var parts = logLine.Split(' ');
        int nameIndexFim = Array.IndexOf(parts, "have") - 1;
        int nameIndexInicio = Array.IndexOf(parts, "[info]") + 1;
        string playerNameCompleto = "";
        for (int i = nameIndexInicio; i <= nameIndexFim; i++)
        {
            playerNameCompleto += parts[i] + " ";
        }
        return playerNameCompleto.TrimEnd();
    }

    // Extrai o nome da guilda da linha de log
    private static string ExtractGuild(string logLine)
    {
        var parts = logLine.Split('\'');
        return parts.Length > 1 ? parts[1] : "Desconhecida";
    }

    // Envia uma notificação para o Discord
    static async Task SendDiscordNotification(string message, string webhookUrl)
    {
        try
        {
            ServicePointManager.ServerCertificateValidationCallback += (sender, certificate, chain, sslPolicyErrors) => true;

            var httpWebRequest = (HttpWebRequest)WebRequest.Create(webhookUrl);
            httpWebRequest.ContentType = "application/json";
            httpWebRequest.Method = "POST";

            using (var streamWriter = new StreamWriter(httpWebRequest.GetRequestStream()))
            {
                string json = new JObject(
                    new JProperty("content", message)
                ).ToString();

                streamWriter.Write(json);
            }

            var httpResponse = (HttpWebResponse)httpWebRequest.GetResponse();
            using (var streamReader = new StreamReader(httpResponse.GetResponseStream()))
            {
                var result = streamReader.ReadToEnd();
                //Console.WriteLine(result);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Erro ao enviar notificação para o Discord: {ex.Message}");
        }
    }
}

public class Guild
{
    public string AdminUID { get; set; }
    public string CampNum { get; set; }
    public Dictionary<string, object> Camps { get; set; }
    public int Level { get; set; }
    public Dictionary<string, GuildMember> Members { get; set; }
    public string Name { get; set; }
}

public class GuildMember
{
    public long Exp { get; set; }
    public int Level { get; set; }
    public string NickName { get; set; }
    public string Pos { get; set; } // Considerando o formato como string, você pode criar uma classe FVector se precisar de um tipo específico
}

public class ApiResponse
{
    public List<Player> Players { get; set; }
}

public class Player
{
    public string Name { get; set; }
}
