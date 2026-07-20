package io.github.mcserverlauncher.bridge;

import java.io.BufferedReader;
import java.io.BufferedWriter;
import java.io.File;
import java.io.FileInputStream;
import java.io.InputStreamReader;
import java.io.OutputStreamWriter;
import java.net.InetAddress;
import java.net.InetSocketAddress;
import java.net.Socket;
import java.nio.charset.StandardCharsets;
import java.time.Instant;
import java.util.ArrayList;
import java.util.Collections;
import java.util.IdentityHashMap;
import java.util.LinkedHashMap;
import java.util.LinkedHashSet;
import java.util.List;
import java.util.Map;
import java.util.Set;
import java.util.concurrent.atomic.AtomicLong;
import java.util.logging.Level;
import org.bukkit.Bukkit;
import org.bukkit.command.Command;
import org.bukkit.command.CommandMap;
import org.bukkit.command.PluginIdentifiableCommand;
import org.bukkit.entity.Player;
import org.bukkit.event.EventHandler;
import org.bukkit.event.Listener;
import org.bukkit.event.player.PlayerJoinEvent;
import org.bukkit.event.player.PlayerQuitEvent;
import org.bukkit.plugin.Plugin;
import org.bukkit.plugin.java.JavaPlugin;

/** 공개 Paper/Bukkit API만 사용해 로컬 런처에 명령 메타데이터를 전달합니다. */
@SuppressWarnings("deprecation")
public final class CommandBridgePlugin extends JavaPlugin implements Listener {
    private static final String SESSION_FILE = ".mcsl-command-bridge-session.json";
    private final Object connectionLock = new Object();
    private final AtomicLong responseSequence = new AtomicLong();
    private volatile boolean stopping;
    private volatile Socket socket;
    private volatile BufferedWriter writer;
    private Thread connectionThread;
	private int metricsTaskId = -1;

    @Override
    public void onEnable() {
        Bukkit.getPluginManager().registerEvents(this, this);
		metricsTaskId = Bukkit.getScheduler().scheduleSyncRepeatingTask(this, this::sendMetrics, 20L, 100L);
        connectionThread = new Thread(this::connectionLoop, "MCSL 명령 브리지 연결");
        connectionThread.setDaemon(true);
        connectionThread.start();
    }

    @Override
    public void onDisable() {
        stopping = true;
		if (metricsTaskId >= 0) Bukkit.getScheduler().cancelTask(metricsTaskId);
        closeConnection();
        if (connectionThread != null) {
            try { connectionThread.join(2000L); } catch (InterruptedException exception) { Thread.currentThread().interrupt(); }
        }
    }

    @EventHandler
    public void onJoin(PlayerJoinEvent event) {
        Bukkit.getScheduler().runTask(this, this::sendPlayers);
    }

    @EventHandler
    public void onQuit(PlayerQuitEvent event) {
        Bukkit.getScheduler().runTask(this, this::sendPlayers);
    }

    private void connectionLoop() {
        while (!stopping) {
            try {
                Map<String, Object> session = readSession();
                if (session == null) { sleepBeforeRetry(); continue; }
                int port = BridgeProtocol.integer(session, "port");
                String token = BridgeProtocol.string(session, "token");
                String profile = BridgeProtocol.string(session, "profile");
                if (port < 1 || port > 65535 || token.length() < 32 || BridgeProtocol.integer(session, "protocol") != BridgeProtocol.PROTOCOL_VERSION) {
                    sleepBeforeRetry();
                    continue;
                }
                Socket candidate = new Socket();
                candidate.connect(new InetSocketAddress(InetAddress.getByName("127.0.0.1"), port), 3000);
                candidate.setSoTimeout(35000);
                candidate.setTcpNoDelay(true);
                BufferedWriter candidateWriter = new BufferedWriter(new OutputStreamWriter(candidate.getOutputStream(), StandardCharsets.UTF_8));
                synchronized (connectionLock) { socket = candidate; writer = candidateWriter; }
                send(BridgeProtocol.object("type", "hello", "id", nextId(), "token", token, "profile", profile, "protocol", BridgeProtocol.PROTOCOL_VERSION, "version", getDescription().getVersion()));
                Bukkit.getScheduler().runTask(this, this::sendPlayers);
                readMessages(candidate);
            } catch (Exception exception) {
                if (!stopping) getLogger().log(Level.FINE, "로컬 런처 명령 브리지 연결을 기다리는 중입니다.", exception);
            } finally {
                closeConnection();
            }
            sleepBeforeRetry();
        }
    }

    private void readMessages(Socket connectedSocket) throws Exception {
        BufferedReader reader = new BufferedReader(new InputStreamReader(connectedSocket.getInputStream(), StandardCharsets.UTF_8));
		int messagesInWindow = 0;
		long windowStarted = System.currentTimeMillis();
        while (!stopping && !connectedSocket.isClosed()) {
            String line = readLimitedLine(reader);
            if (line == null) return;
            Map<String, Object> message;
            try {
                message = BridgeProtocol.parseObject(line);
            } catch (RuntimeException exception) {
                sendError("0", "invalid-json");
                return;
            }
            String type = BridgeProtocol.string(message, "type");
            String id = BridgeProtocol.string(message, "id");
			long now = System.currentTimeMillis();
			if (now - windowStarted >= 1000L) { windowStarted = now; messagesInWindow = 0; }
			if (++messagesInWindow > 120) { sendError(id, "rate-limit"); return; }
            if (id.length() == 0 || id.length() > 128) { sendError("0", "invalid-id"); continue; }
            if ("capabilities".equals(type) || "pong".equals(type)) continue;
            if ("ping".equals(type)) { send(BridgeProtocol.object("type", "pong", "id", id)); continue; }
            if ("command-list-request".equals(type)) {
                Bukkit.getScheduler().runTask(this, () -> sendCommandList(id));
            } else if ("suggest-request".equals(type)) {
                String input = BridgeProtocol.string(message, "input");
                if (input.length() > 2048 || input.indexOf('\n') >= 0 || input.indexOf('\r') >= 0) sendError(id, "invalid-input");
                else Bukkit.getScheduler().runTask(this, () -> sendSuggestions(id, input));
            } else {
                sendError(id, "unsupported-message");
            }
        }
    }

	@SuppressWarnings("deprecation")
    private void sendCommandList(String id) {
        try {
            CommandMap commandMap = Bukkit.getServer().getCommandMap();
            Map<Command, CommandEntry> byIdentity = new IdentityHashMap<Command, CommandEntry>();
            for (Map.Entry<String, Command> known : commandMap.getKnownCommands().entrySet()) {
                Command command = known.getValue();
                if (command == null || !command.testPermissionSilent(Bukkit.getConsoleSender())) continue;
                CommandEntry entry = byIdentity.get(command);
                if (entry == null) {
                    entry = new CommandEntry(command);
                    byIdentity.put(command, entry);
                }
                entry.addKnownName(known.getKey());
            }
            List<Object> values = new ArrayList<Object>();
            for (CommandEntry entry : byIdentity.values()) values.add(entry.toJson());
            values.sort((left, right) -> BridgeProtocol.string(castObject(left), "name").compareToIgnoreCase(BridgeProtocol.string(castObject(right), "name")));
            send(BridgeProtocol.object("type", "command-list-response", "id", id, "commands", values));
        } catch (Throwable exception) {
            getLogger().log(Level.WARNING, "명령 목록을 수집하지 못했습니다.", exception);
            sendError(id, "command-list-failed");
        }
    }

	@SuppressWarnings("deprecation")
    private void sendSuggestions(String id, String input) {
        List<String> suggestions = new ArrayList<String>();
        try {
            List<String> provided = Bukkit.getServer().getCommandMap().tabComplete(Bukkit.getConsoleSender(), input);
            if (provided != null) {
                Set<String> unique = new LinkedHashSet<String>();
                for (String item : provided) {
                    if (item == null || item.length() == 0 || item.length() > 512) continue;
                    unique.add(item);
                    if (unique.size() >= BridgeProtocol.MAXIMUM_SUGGESTIONS) break;
                }
                suggestions.addAll(unique);
            }
        } catch (Throwable exception) {
            // 잘못 구현된 외부 플러그인 탭 완성기는 이 요청에만 격리합니다.
            getLogger().log(Level.FINE, "외부 명령 자동완성기가 오류를 반환했습니다.", exception);
        }
        send(BridgeProtocol.object("type", "suggest-response", "id", id, "suggestions", suggestions));
    }

    private void sendPlayers() {
        List<String> players = new ArrayList<String>();
        for (Player player : Bukkit.getOnlinePlayers()) players.add(player.getName());
        Collections.sort(players, String.CASE_INSENSITIVE_ORDER);
        send(BridgeProtocol.object("type", "players-update", "id", nextId(), "players", players));
    }

	/** Paper/Purpur가 공개하는 틱 지표만 전달하며, 없는 API의 값은 추정하지 않습니다. */
	private void sendMetrics() {
		try {
			Object server = Bukkit.getServer();
			double[] tps = (double[]) server.getClass().getMethod("getTPS").invoke(server);
			Number averageTickTime = (Number) server.getClass().getMethod("getAverageTickTime").invoke(server);
			if (tps == null || tps.length < 3 || averageTickTime == null) throw new IllegalStateException("metrics-unavailable");
			double tps1 = requireFiniteNonNegative(tps[0]);
			double tps5 = requireFiniteNonNegative(tps[1]);
			double tps15 = requireFiniteNonNegative(tps[2]);
			double mspt = requireFiniteNonNegative(averageTickTime.doubleValue());
			send(BridgeProtocol.object("type", "metrics-update", "id", nextId(), "supported", true, "tps1", tps1, "tps5", tps5, "tps15", tps15, "mspt", mspt));
		} catch (Throwable unavailable) {
			// Bukkit 구현에 지표 API가 없으면 지원 불가 상태를 명시하고 임의 값을 만들지 않습니다.
			send(BridgeProtocol.object("type", "metrics-update", "id", nextId(), "supported", false));
		}
	}

	private static double requireFiniteNonNegative(double value) {
		if (Double.isNaN(value) || Double.isInfinite(value) || value < 0.0) throw new IllegalArgumentException("invalid-metric");
		return value;
	}

    private Map<String, Object> readSession() {
        File serverDirectory = getDataFolder().getParentFile().getParentFile();
        File sessionFile = new File(serverDirectory, SESSION_FILE);
        if (!sessionFile.isFile() || sessionFile.length() < 20 || sessionFile.length() > 8192) return null;
        try {
            BufferedReader reader = new BufferedReader(new InputStreamReader(new FileInputStream(sessionFile), StandardCharsets.UTF_8));
            try {
                StringBuilder value = new StringBuilder();
                String line;
                while ((line = reader.readLine()) != null) {
                    value.append(line);
                    if (value.length() > 8192) return null;
                }
				Map<String, Object> session = BridgeProtocol.parseObject(value.toString());
				String expiresUtc = BridgeProtocol.string(session, "expiresUtc");
				if (expiresUtc.length() == 0 || Instant.parse(expiresUtc).isBefore(Instant.now())) return null;
				return session;
            } finally { reader.close(); }
        } catch (Exception ignored) {
            return null;
        }
    }

    private String readLimitedLine(BufferedReader reader) throws Exception {
        StringBuilder result = new StringBuilder();
        while (true) {
            int value = reader.read();
            if (value < 0) return result.length() == 0 ? null : result.toString();
            if (value == '\n') return result.toString();
            if (value != '\r') result.append((char) value);
            if (result.length() > BridgeProtocol.MAXIMUM_LINE_LENGTH) throw new IllegalArgumentException("request-too-large");
        }
    }

    private void sendError(String id, String message) {
        send(BridgeProtocol.object("type", "error", "id", id, "message", message));
    }

    private void send(Map<String, Object> message) {
        String json;
        try { json = BridgeProtocol.json(message); } catch (RuntimeException ignored) { return; }
        synchronized (connectionLock) {
            if (writer == null) return;
            try { writer.write(json); writer.newLine(); writer.flush(); } catch (Exception ignored) { closeConnection(); }
        }
    }

    private void closeConnection() {
        synchronized (connectionLock) {
            try { if (socket != null) socket.close(); } catch (Exception ignored) { }
            socket = null;
            writer = null;
        }
    }

    private void sleepBeforeRetry() {
        if (stopping) return;
        try { Thread.sleep(2000L); } catch (InterruptedException exception) { Thread.currentThread().interrupt(); }
    }

    private String nextId() {
        return "p" + responseSequence.incrementAndGet();
    }

    @SuppressWarnings("unchecked")
    private static Map<String, Object> castObject(Object value) {
        return (Map<String, Object>) value;
    }

    private static final class CommandEntry {
        private final Command command;
        private final Set<String> names = new LinkedHashSet<String>();

        CommandEntry(Command value) {
            command = value;
        }

        void addKnownName(String value) {
            if (value != null && value.indexOf(':') < 0) names.add(value);
        }

        Map<String, Object> toJson() {
            String name = command.getName();
			if (name == null || name.length() == 0) name = names.isEmpty() ? "unknown" : names.iterator().next();
            Set<String> aliases = new LinkedHashSet<String>();
            aliases.addAll(command.getAliases());
            aliases.addAll(names);
            aliases.remove(name);
            String plugin = "";
            if (command instanceof PluginIdentifiableCommand) {
                Plugin owner = ((PluginIdentifiableCommand) command).getPlugin();
                if (owner != null) plugin = owner.getName();
            }
            return BridgeProtocol.object("name", name, "aliases", new ArrayList<String>(aliases), "description", command.getDescription(), "usage", command.getUsage(), "plugin", plugin);
        }
    }
}
