using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Collections.Concurrent;
using System.Net;
using System.Text;

class Program
{

    private static readonly ConcurrentDictionary<string, long> filePositions = new ConcurrentDictionary<string, long>();
    private static string currentLogFile = null;

    static string discordWebhookUrl_Logs;
    static string discordWebhookUrl_Cheater;
    static string discordWebhookUrl_AtaquesGuild;

    static string GuildExportFilePath;
    static string PlayersApiUrl;
    private static string KickApiUrl;
    private static string usuarioServidorPal;
    private static string senhaServidorPal;

    static string logDirectory;
    static string dataDirectory;

    private static int TempoEntreMsg;
    private static readonly TimeSpan CooldownPeriod;
    private static Dictionary<string, DateTime> _recentLogs = new Dictionary<string, DateTime>();
    private static readonly object Lock = new object();
    static int semLogsEnviados = 0;
    public static KickPlayersManager kickPlayersManager = new KickPlayersManager();
    private static Dictionary<string, DateTime> raidTimers = new Dictionary<string, DateTime>();
    private static int raidDurationInHours;

    static async Task Main(string[] args)
    {
        // Diretórios
        logDirectory = Environment.GetEnvironmentVariable("LOG_DIRECTORY") ?? @"\\192.168.100.73\palguard\logs";
        dataDirectory = Environment.GetEnvironmentVariable("DATA_DIRECTORY") ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Dados");
        GuildExportFilePath = Environment.GetEnvironmentVariable("PAL_GUARD_DIRECTORY") ?? @"\\OPTSUKE01\steamcmd\steamapps\common\PalServer\Pal\Binaries\Win64\palguard\guildexport.json";

        // URLs das APIs
        PlayersApiUrl = Environment.GetEnvironmentVariable("PLAYERS_API_URL") ?? "http://192.168.100.73:8212/v1/api/players";
        KickApiUrl = Environment.GetEnvironmentVariable("KICK_API_URL") ?? "http://192.168.100.73:8212/v1/api/kick";

        // Credenciais do servidor
        usuarioServidorPal = Environment.GetEnvironmentVariable("USUARIO_SERVIDOR_PAL") ?? "admin";
        senhaServidorPal = Environment.GetEnvironmentVariable("SENHA_SERVIDOR_PAL") ?? "unreal";

        // URLs dos Webhooks do Discord
        discordWebhookUrl_Logs = Environment.GetEnvironmentVariable("DISCORD_WEBHOOK_URL_Logs") ?? "https://discord.com/api/webhooks/1264730050935263273/dFORkiYBVRu0muxAp8V6Kvj9_nmTaCjn_I1SXH_FrXmhdm1ZiEKE1MMIL5Xd3DhiNOAe";
        discordWebhookUrl_Cheater = Environment.GetEnvironmentVariable("DISCORD_WEBHOOK_URL_Cheater") ?? "https://discord.com/api/webhooks/1264728493967671446/Bp-gXNAH__HISjDMeSkYyIane-iQ38HE9QhIkXhPqq8DrTXErfGQThhA6YZoy3EZgu3a";
        discordWebhookUrl_AtaquesGuild = Environment.GetEnvironmentVariable("DISCORD_WEBHOOK_URL_AtaquesGuild") ?? "https://discord.com/api/webhooks/1264728721835954196/ZZRHjttkYbN-WL1EkgWntgdZtLuuijeFFafSY0xXytbhukvgJpRmuGpz2qKPEY5QgmwS";

        // Configurações de raid e cooldown
        raidDurationInHours = int.Parse(Environment.GetEnvironmentVariable("RAID_DURATION") ?? "2");
        TempoEntreMsg = int.Parse(Environment.GetEnvironmentVariable("COOLDOWN_MSG_API") ?? "2");

        // Cria o diretório de dados se ele não existir
        if (!Directory.Exists(dataDirectory))
        {
            Directory.CreateDirectory(dataDirectory);
        }

        //criando lista de kick



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
            Thread.Sleep(120000);
            MonitorLogFile(currentLogFile, false);
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

        while (true)
        {
            try
            {
                await ReadLogFile(filePath, isFirstRead);
                isFirstRead = false;
                await Task.Delay(1000);
                if (semLogsEnviados > 10)
                {
                    semLogsEnviados = 0;
                    //Console.WriteLine("Procurando novo arquivo");

                    FileSystemWatcher watcher = new FileSystemWatcher
                    {
                        Path = logDirectory,
                        Filter = "*.txt",
                        NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite
                    };

                    watcher.Created += OnChanged;
                    watcher.Changed += OnChanged;

                    watcher.EnableRaisingEvents = true;
                }
                //Console.WriteLine(semLogsEnviados + " Segundos :DEBUG: Contando tempo sem log");
                semLogsEnviados++;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erro ao monitorar o arquivo: {ex.Message}");
                Thread.Sleep(120000);
            }
        }

    }

    // Lê o arquivo de log a partir da posição onde foi lido pela última vez
    private static async Task ReadLogFile(string filePath, bool isFirstRead)
    {
        while (true)
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

                        ProcessLogLine(lastLine);
                        filePositions[filePath] = fs.Position;
                    }
                    else
                    {
                        fs.Seek(filePositions.GetOrAdd(filePath, 0), SeekOrigin.Begin);
                        while ((logLine = await reader.ReadLineAsync()) != null)
                        {

                            ProcessLogLine(logLine);
                            semLogsEnviados = 0;
                        }
                        filePositions[filePath] = fs.Position;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erro ao ler o arquivo: {ex.Message}");
                Thread.Sleep(120000);
            }
        }
    }

    // Processa cada linha do log
    private static async Task ProcessLogLine(string logLine)
    {
        string formattedMessage = null;
        Console.WriteLine(logLine);

        if (logLine.Contains("have dealt"))
        {
            formattedMessageClass Mensagem = await FormatAttackMessage(logLine);
            if (Mensagem != null)
            {
                if (await ProcessLogMessage(Mensagem.message, discordWebhookUrl_AtaquesGuild))
                {
                    if (Mensagem.menbrosOnline != "Nenhum membro online")
                    {
                        // Inicia ou reseta o contador de 2h para Mensagem.guildaAtacada
                        if (raidTimers.ContainsKey(Mensagem.guildaAtacadaId))
                        {
                            raidTimers[Mensagem.guildaAtacadaId] = DateTime.Now.AddHours(raidDurationInHours);
                            raidTimers[Mensagem.guildaAtacanteId] = DateTime.Now.AddHours(raidDurationInHours);
                        }
                        else
                        {
                            raidTimers.Add(Mensagem.guildaAtacadaId, DateTime.Now.AddHours(raidDurationInHours));
                            raidTimers.Add(Mensagem.guildaAtacanteId, DateTime.Now.AddHours(raidDurationInHours));
                        }
                    }
                    else
                    {
                        if (!raidTimers.ContainsKey(Mensagem.guildaAtacanteId))
                        {
                            kickPlayersManager.AddKickedPlayer(Mensagem.attackerName, Mensagem.attackerId, "Kick por dano offline");
                        }

                    }
                }
            }
        }
        else if (logLine.Contains("is a cheater"))
        {
            formattedMessage = logLine;
            await ProcessLogMessage(formattedMessage, discordWebhookUrl_Cheater);
        }

        // Envia a mensagem original para o webhook de logs
        await ProcessLogMessage(logLine, discordWebhookUrl_Logs);
        KickPlayersInList();

        // Verifica e gerencia o tempo restante dos ataques
        ManageRaidTimers();
    }

    // Formata a mensagem de ataque
    private static async Task<formattedMessageClass> FormatAttackMessage(string logLine)
    {
        // Carrega os dados das guildas do arquivo JSON
        var guildData = LoadGuildData(GuildExportFilePath);
        // Obtém a lista de jogadores online
        var onlinePlayers = await GetOnlinePlayers();
        var onlineNamePlayers = new List<string>();

        foreach (var onlinePlayer in onlinePlayers)
        {
            onlineNamePlayers.Add(onlinePlayer.Name);
        }

        // Extrai as informações da linha do log
        string time = logLine.Substring(1, 8);
        string attacker = ExtractAttackerName(logLine);
        string attackerId = "";
        string attackerGuild = "";
        string attackerGuildId = "";

        foreach (var onlinePlayer in onlinePlayers)
        {
            if (onlinePlayer.Name.Contains(attacker))
            {
                attackerId = onlinePlayer.UserId;
                break;
            }
        }

        string guildName = ExtractGuild(logLine);

        if (guildName == "Guilda sem nome")
        {
            return null;
        }

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
                    if (onlineNamePlayers.Contains(member.Value.NickName))
                    {
                        onlineMembers.Add(member.Value.NickName);

                    }
                }
                foreach (var atacante in guildData.Guilds)
                {
                    foreach (var member in atacante.Value.Members)
                    {
                        if (attacker == member.Value.NickName)
                        {
                            attackerGuild = atacante.Value.Name;
                            attackerGuildId = atacante.Key;
                            break;
                        }
                    }
                }


                // Cria uma lista formatada com os membros online
                var onlineMembersList = onlineMembers.Any() ? string.Join(", ", onlineMembers) : "Nenhum membro online";

                // Retorna a mensagem formatada
                formattedMessageClass messageReturn = new();
                messageReturn.message = $"{time} {attacker} ({attackerGuild}) atacou a guilda {guildEntry.Value.Name} ({onlineMembersList})";
                messageReturn.guildaAtacada = guildEntry.Value.Name;
                messageReturn.menbrosOnline = onlineMembersList;
                messageReturn.guildaAtacadaId = guildEntry.Key;
                messageReturn.attackerId = attackerId;
                messageReturn.attackerName = attacker;
                messageReturn.guildaAtacanteId = attackerGuildId;
                Console.WriteLine(messageReturn.message);
                return messageReturn;
            }
        }

        // Retorna null se a guilda não for encontrada
        return null;
    }

    private static string ManageRaidTimers()
    {
        List<string> guildasToRemove = new List<string>();

        foreach (var guilda in raidTimers.Keys)
        {
            TimeSpan quantoTempo = (raidTimers[guilda] - DateTime.Now);
            if (raidTimers[guilda] <= DateTime.Now)
            {
                guildasToRemove.Add(guilda);
            }
        }

        foreach (var guilda in guildasToRemove)
        {
            raidTimers.Remove(guilda);
        }
        return (string.Join(Environment.NewLine, raidTimers.Select(rt => $"Guilda: {rt.Key}, Timer: {rt.Value}")));
    }

    public static async Task KickPlayerNow(string userId, string message, string name)
    {
        var client = new HttpClient();
        var request = new HttpRequestMessage(HttpMethod.Post, KickApiUrl);

        // Converter login e senha para Base64
        var login = usuarioServidorPal;
        var senha = senhaServidorPal;
        var authToken = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{login}:{senha}"));

        request.Headers.Add("Authorization", $"Basic {authToken}");

        var kickRequest = new
        {
            userid = userId,
            message = message
        };

        var content = new StringContent(JsonConvert.SerializeObject(kickRequest), Encoding.UTF8, "application/json");
        request.Content = content;

        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        Console.WriteLine(name + " " + userId + " " + message);
        // Console.WriteLine(await response.Content.ReadAsStringAsync());
    }

    public static void KickPlayersInList()
    {
        //Console.WriteLine(kickPlayersManager.KickedPlayers.Count);
        foreach (var kickedPlayer in kickPlayersManager.KickedPlayers)
        {
            Console.WriteLine($"{kickedPlayer.UserId} {kickedPlayer.Message} {kickedPlayer.Name}");
            KickPlayerNow(kickedPlayer.UserId, kickedPlayer.Message, kickedPlayer.Name);
            kickPlayersManager.RemoveKickedPlayer(kickedPlayer.UserId);
            Thread.Sleep(1000);
        }
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

    private static async Task<List<PlayerInfo>> GetOnlinePlayers()
    {
        var client = new HttpClient();
        var request = new HttpRequestMessage(HttpMethod.Get, PlayersApiUrl);
        var login = "admin";
        var senha = "unreal";
        var authToken = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{login}:{senha}"));
        request.Headers.Add("Authorization", $"Basic {authToken}");
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var responseBody = await response.Content.ReadAsStringAsync();

        var apiResponse = JsonConvert.DeserializeObject<ApiResponse>(responseBody);
        return apiResponse.Players.Select(p => new PlayerInfo { Name = p.Name, UserId = p.UserId }).ToList();
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
    static async Task<bool> ProcessLogMessage(string message, string webhookUrl)
    {
        string[] parts = message.Split(new[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);

        lock (Lock)
        {
            if (_recentLogs.TryGetValue(parts[1], out var lastTimestamp))
            {
                if (DateTime.Now - lastTimestamp < CooldownPeriod)
                {
                    // Mensagem repetida dentro do período de cooldown, ignorar
                    //Console.WriteLine($"Mensagem repetida, ignorando -- ({message})");
                    return false;
                }
            }

            // Processar a mensagem (enviar para o Discord, etc.)
            //Console.WriteLine(message);

            SendDiscordNotification(message, webhookUrl);

            // Atualizar o timestamp da mensagem
            _recentLogs[parts[1]] = DateTime.Now;

            // Remover entradas antigas do cache
            RemoveOldEntries();
            return true;
        }
    }
    private static void RemoveOldEntries()
    {
        var threshold = DateTime.Now - CooldownPeriod;
        var keysToRemove = new List<string>();

        foreach (var kvp in _recentLogs)
        {
            if (kvp.Value < threshold)
            {
                keysToRemove.Add(kvp.Key);
            }
        }

        foreach (var key in keysToRemove)
        {
            _recentLogs.Remove(key);
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
    public string UserId { get; set; }
    // Outros campos podem ser adicionados aqui conforme necessário
}
public class PlayerInfo
{
    public string Name { get; set; }
    public string UserId { get; set; }
}
public class KickPlayersManager
{
    public List<KickPlayersAttakerOff> KickedPlayers { get; private set; } = new List<KickPlayersAttakerOff>();

    public void AddKickedPlayer(string name, string userId, string message)
    {
        if (!KickedPlayers.Any(p => p.UserId == userId))
        {
            KickedPlayers.Add(new KickPlayersAttakerOff { Name = name, UserId = userId, Message = message });
        }
    }

    public void RemoveKickedPlayer(string userId)
    {
        var player = KickedPlayers.FirstOrDefault(p => p.UserId == userId);
        if (player != null)
        {
            Console.WriteLine(player.Name + " Removido da Lista");
            KickedPlayers.Remove(player);
        }
    }

    public void ClearKickedPlayers()
    {
        KickedPlayers.Clear();
    }
}

public class KickPlayersAttakerOff
{
    public string UserId { get; set; }
    public string Name { get; set; }
    public string Message { get; set; }
}
public class formattedMessageClass
{
    public string message { get; set; }
    public string attackerId { get; set; }
    public string attackerName { get; set; }
    public string guildaAtacadaId { get; set; }
    public string menbrosOnline { get; set; }
    public string guildaAtacada { get; set; }
    public string guildaAtacanteId { get; set; }


}

