package io.github.mcserverlauncher.bridge;

import java.util.ArrayList;
import java.util.List;
import java.util.Map;

/** 외부 테스트 프레임워크 없이 실행하는 브리지 프로토콜 회귀 테스트입니다. */
public final class BridgeProtocolTest {
    private static int passed;

    public static void main(String[] args) {
        Map<String, Object> parsed = BridgeProtocol.parseObject("{\"type\":\"suggest-request\",\"id\":\"s1\",\"input\":\"give \\\"Alex Smith\\\"\"}");
        equal("suggest-request", BridgeProtocol.string(parsed, "type"), "메시지 종류");
        equal("give \"Alex Smith\"", BridgeProtocol.string(parsed, "input"), "이스케이프 문자열");
        Map<String, Object> roundTrip = BridgeProtocol.parseObject(BridgeProtocol.json(BridgeProtocol.object("type", "pong", "id", "p1", "protocol", 1)));
        equal("pong", BridgeProtocol.string(roundTrip, "type"), "JSON 왕복");
        equal(1, BridgeProtocol.integer(roundTrip, "protocol"), "프로토콜 정수");
		Map<String, Object> metrics = BridgeProtocol.parseObject(BridgeProtocol.json(BridgeProtocol.object("type", "metrics-update", "id", "m1", "supported", true, "tps1", 19.95, "mspt", 12.5)));
		equal("true", BridgeProtocol.string(metrics, "supported"), "지표 지원 상태");
		equal("19.95", BridgeProtocol.string(metrics, "tps1"), "TPS 소수 JSON 왕복");
        expectFailure(() -> BridgeProtocol.parseObject("not-json"), "잘못된 JSON 차단");
        expectFailure(() -> BridgeProtocol.parseObject("{} trailing"), "뒤따르는 데이터 차단");
        expectFailure(() -> BridgeProtocol.parseObject(new String(new char[BridgeProtocol.MAXIMUM_LINE_LENGTH + 1]).replace('\0', 'x')), "요청 크기 제한");
        List<String> suggestions = new ArrayList<String>();
        for (int index = 0; index < BridgeProtocol.MAXIMUM_SUGGESTIONS; index++) suggestions.add("value-" + index);
        String response = BridgeProtocol.json(BridgeProtocol.object("type", "suggest-response", "id", "s2", "suggestions", suggestions));
        if (response.length() >= BridgeProtocol.MAXIMUM_LINE_LENGTH) throw new IllegalStateException("정상 후보 응답이 크기 제한을 넘었습니다.");
        passed++;
        System.out.println("BRIDGE_PROTOCOL_PASSED=" + passed);
    }

    private static void equal(Object expected, Object actual, String name) {
        if (!expected.equals(actual)) throw new IllegalStateException(name + ": expected=" + expected + ", actual=" + actual);
        passed++;
    }

    private static void expectFailure(Runnable action, String name) {
        try { action.run(); } catch (RuntimeException expected) { passed++; return; }
        throw new IllegalStateException(name + " 검사가 실패를 차단하지 못했습니다.");
    }
}
