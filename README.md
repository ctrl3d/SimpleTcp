# SimpleTcp

Unity에서 TCP 서버와 클라이언트를 간단히 붙이기 위한 패키지입니다.

`0.2.0`부터는 큰 문자열과 byte 배열을 안정적으로 보내기 위해 `type + length + payload` 프레임 프로토콜을 사용합니다. TCP chunk가 여러 번으로 쪼개져도 수신 이벤트는 완성된 메시지 단위로 1번만 호출됩니다.

## 설치

Unity Package Manager의 Git URL에 아래 주소를 입력합니다.

```text
https://github.com/ctrl3d/SimpleTcp.git?path=Assets/SimpleTcp
```

## 빠른 사용법

### 서버

```csharp
using work.ctrl3d;

var server = new SimpleTcpServer();
server.MaxMessageSize = (int)SimpleTcpMessageSize.MiB32;

server.OnClientConnected += id => UnityEngine.Debug.Log($"Connected: {id}");
server.OnMessageReceived += (id, message) => UnityEngine.Debug.Log(message);
server.OnBytesReceived += (id, bytes) => UnityEngine.Debug.Log($"{bytes.Length} bytes");

server.Start("0.0.0.0", 7777);
server.SendToClient(clientId, "hello");
server.SendBytesToClient(clientId, bytes);
server.Broadcast("hello everyone");
```

### 클라이언트

```csharp
using work.ctrl3d;

var client = new SimpleTcpClient();
client.MaxMessageSize = (int)SimpleTcpMessageSize.MiB32;

client.OnConnected += () => UnityEngine.Debug.Log("Connected");
client.OnMessageReceived += message => UnityEngine.Debug.Log(message);
client.OnBytesReceived += bytes => UnityEngine.Debug.Log($"{bytes.Length} bytes");

await client.ConnectAsync("127.0.0.1", 7777);
await client.SendStringAsync("hello");
await client.SendBytesAsync(bytes);
```

`0.3.0`부터 클라이언트 연결은 비동기로 처리됩니다. `Connect()`는 호출 스레드를 블로킹하지 않고 내부에서 `ConnectAsync()`를 시작한 뒤 즉시 반환합니다. 연결 직후 바로 송신해야 하면 `ConnectAsync()`를 `await`하거나 `OnConnected` 이벤트 이후 송신하세요.

`ConnectAsync`, `SendStringAsync`, `SendBytesAsync`, `SendToClientAsync`, `BroadcastAsync`도 제공됩니다. 완료나 실패 흐름을 직접 제어해야 하면 `async` 버전을 쓰는 편이 좋습니다.

## Unity 컴포넌트

프리팹 또는 직접 추가한 컴포넌트로도 사용할 수 있습니다.

- `UnitySimpleTcpServer`: 서버 시작, 클라이언트 송신, 브로드캐스트, UnityEvent 제공
- `UnitySimpleTcpClient`: 서버 연결, 문자열/byte 송신, 자동 재연결, UnityEvent 제공

Inspector의 `maxMessageSize`는 한 메시지의 최대 payload 크기입니다. `MiB1`, `MiB5`, `MiB8`, `MiB16`, `MiB32`, `MiB64`, `MiB128` 중에서 선택할 수 있고 기본값은 `MiB32`입니다.

`UnitySimpleTcpClient`의 시작 연결과 자동 재연결도 비동기 연결을 사용하므로, 서버가 꺼져 있거나 연결 실패가 반복되어도 Unity 메인 스레드를 직접 막지 않습니다.

## 전송 방식

한 메시지는 다음 형식으로 전송됩니다.

```text
1 byte  : payload type
4 bytes : payload length, big-endian
N bytes : payload
```

payload type은 `1 = string`, `2 = bytes`입니다. 문자열은 UTF-8로 인코딩됩니다.

## 주의

- `0.2.0` 프로토콜은 `0.1.x`의 raw TCP 문자열 전송 방식과 호환되지 않습니다.
- 서버와 클라이언트는 같은 버전의 SimpleTcp를 사용하는 것이 안전합니다.
- base64 문자열도 보낼 수 있지만, 바이너리 데이터는 가능하면 `SendBytes`로 보내는 편이 메모리와 용량 면에서 낫습니다.
- 큰 메시지를 로그로 모두 출력하지 않도록 Unity 래퍼는 로그를 앞부분만 표시합니다.
- 상용 기본값은 용도에 따라 `MiB8` 또는 `MiB16`으로 낮추는 편이 더 안전합니다.
- `Connect()`는 비동기로 연결을 시작하고 즉시 반환합니다. 연결 완료가 필요한 흐름은 `ConnectAsync()` 또는 `OnConnected`를 사용하세요.

## 버전 관리

이 패키지는 SemVer 형식을 따릅니다.

- `MAJOR`: 안정화 이후 호환이 깨지는 변경
- `MINOR`: 기능 추가 또는 `0.x` 단계의 큰 내부 변경
- `PATCH`: 버그 수정

현재 버전은 `0.3.0`입니다.
