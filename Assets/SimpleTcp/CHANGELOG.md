# Changelog

## 0.2.0 - 2026-07-02

### 변경

- TCP 송수신을 `type + length + payload` 프레임 방식으로 변경했습니다.
- 큰 문자열과 byte 배열을 완성된 메시지 단위로 받을 수 있게 했습니다.
- `SendStringAsync`, `SendBytesAsync`, `SendToClientAsync`, `BroadcastAsync` 계열 API를 추가했습니다.
- `OnBytesReceived`, `SendBytesToClient`, `BroadcastBytes`를 추가했습니다.
- `MaxMessageSize` 설정을 추가했습니다. 기본값은 `32 MiB`입니다.
- Unity Inspector에서 메시지 크기를 고르기 위한 `SimpleTcpMessageSize` preset enum을 추가했습니다.
- Unity 래퍼의 큰 문자열 로그를 512자 preview로 제한했습니다.

### 호환성

- `0.2.0`은 `0.1.x` raw TCP 문자열 프로토콜과 호환되지 않습니다.
- 서버와 클라이언트를 모두 `0.2.0` 이상으로 맞춰야 합니다.

## 0.1.0

### 추가

- 초기 TCP 서버/클라이언트 구현을 추가했습니다.
- Unity 래퍼와 프리팹을 추가했습니다.
