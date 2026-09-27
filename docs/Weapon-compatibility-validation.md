# 1.0.11 한손 무기 병용 설정 검증

2026-09-27, 최종 병합 DLL 기준. 기존 1.0 대응 변경은 `Valheim-1.0.7-compatibility.md`에 별도로 기록되어 있다.

## 동작 계약

`2 - Shield Actions With One-Handed Weapon`의 세 설정은 기존 ServerSync 동기화 및 설정 잠금을 사용한다.

| 설정 | 기본값 | 병용 시 동작 |
| --- | --- | --- |
| `Allow Shield Charge With One-Handed Weapon` | Off | 방어 + 보조공격으로 돌진 |
| `Allow Block Charge With One-Handed Weapon` | On | 방어 충전 축적과 충전 반격 |
| `Allow Projectile Reflection With One-Handed Weapon` | On | 방어 중 투사체 반사 |

- 왼손의 실제 방패와 오른손의 한손 근접무기가 필요하다. Off여도 오른손이 비어 있으면 허용한다. 각 YAML 기능의 활성화 조건도 충족해야 한다.
- 기본 공격이 근접 공격인 창은 투사체 보조공격이 있어도 허용한다. 횃불·활·양손무기·Tool 분류 및 원거리 기본 공격은 제외한다.
- 무기 기본 공격과 방어 없는 보조공격은 유지한다. 방패 기본 공격과 투척은 오른손이 비어 있어야 한다.
- 돌진으로 선택된 입력은 성공·실패와 관계없이 누르고 있는 동안 한 번만 시도한다. 방어 해제·장비/정책 변경으로 무기 보조공격에 넘어가지 않으며, 해제할 때 해당 보조공격 큐도 지운다.
- 실제 방어 상태, 공격·회피·이동불가·넉백·경직·소동작, 방패 내구도 및 기존 스태미나·쿨다운 조건을 확인한다. 상태 차단 보완은 기존 직접 돌진 경로에도 적용된다.
- 충전 반격은 `Humanoid.BlockAttack`의 `m_buildBlockCharges` 조건 하나에 플레이어별 정책을 적용한다. 공유 아이템 데이터에 장비별 값을 쓰지 않는다. 금지 장비·YAML 비활성화·정의 누락·방패 해제 시 잔여 충전을 지운다.
- 반사는 기존 소유권·발신자·중복 이벤트 검사를 유지한다. 전송 중 반사가 금지되어도 수신한 일반 피해는 처리한다. RPC 버전/저장 형식과 공개 연동 API는 이번 설정 추가로 바뀌지 않는다.
- 입력/충전 상태는 약한 참조로 보관한다. 장면 검색이나 새로운 프레임별 UI 생성은 추가하지 않는다. 충전 정책의 정의 조회에는 Unity prefab 이름 접근이 있어 완전한 무할당 경로는 아니다.

## 수행한 빌드와 자동 검사

- `dotnet build CaptainValheim.csproj -c Debug -p:DeployToGame=true`: 경고 0, 오류 0. 병합 후 게임 plugins 복사본과 SHA-256 일치.
- `dotnet build CaptainValheim.csproj -c Release`: 경고 0, 오류 0. 최종 DLL 어셈블리 버전 `1.0.11.0`.
- Release DLL, 설치된 DLL, Thunderstore/Nexus ZIP 내부 DLL의 SHA-256 일치: `8E0A21C583F1EEB87FAA39B1D4186912A6A9235C64D283B7F774C77C8C36A831`.
- Thunderstore ZIP의 필수 6개 파일, README/CHANGELOG/번역 원본 일치, manifest `1.0.11` 및 `denikson-BepInExPack_Valheim-5.4.2351` 확인. Nexus ZIP은 동일 DLL 1개.
- 최종 Release DLL을 다음 **원본** Valheim 1.0.16 DLL에 각각 대조했다. publicize하지 않았으며 이번 기능을 위해 전체 게임 비교/추출을 다시 수행하지 않았다.

| 역할 | 전역 자료 폴더의 원본 스냅샷 |
| --- | --- |
| 클라이언트 | `client-b25527674-windows-x64-20260925T211211Z/original/valheim_Data/Managed` |
| 데디케이트 서버 | `dedicated-server-b25527701-windows-x64-20260925T211211Z-depot-restored/original/valheim_server_Data/Managed` |

각 역할에서 게임 멤버 참조 **338개**, Harmony 대상 **37개**, 명시적 리플렉션 계약 **32개**, 오류 **0개**. 별도 .NET 10 검사 호스트에서 실제 관리형 패킷 메서드 **36개**, 장비 허용/입력 상태 **52개** assertion 통과. 설정 기본값/동기화 IL 계약 **3개**, transpiler의 원본 조건·스택 시그니처·생성 명령 정적 계약 **9개** 통과.

재실행 예시(저장소 루트; 실제 원본 Managed 경로를 지정):

```powershell
dotnet run --project tools/CompatibilityChecks -- bin/Release/CaptainValheim.dll "C:/Program Files (x86)/Steam/steamapps/common/Valheim/valheim_Data/Managed" "C:/Program Files (x86)/Steam/steamapps/common/Valheim/BepInEx/core" "$env:TEMP/CaptainValheim-release-contracts.json"
```

이 검사는 원본 메타데이터와 순수 관리형 로직에 대한 검사다. .NET 10 호스트에서 설치된 Harmony의 `AccessTools` 초기화가 실패하므로 위 transpiler 검사는 정적 검사이며, 실제 패치 설치 결과로 보지 않는다. .NET Framework 보조 실행도 원본 게임의 기본 인터페이스 구현을 지원하지 못해 타입 로딩에서 실패했다. 게임 DLL의 접근 제한이나 인터페이스를 변경해 우회하지 않았다.

## 아직 필요한 실제 실행 확인

게임·호스트·데디케이트 서버를 새로 실행하여 검증하지 않았다. ServerSync 설정 일치는 서버의 게임플레이 권한 검증이나 치트 방지 추가를 뜻하지 않는다.

| 실행 시나리오 | 확인할 결과 |
| --- | --- |
| 각 옵션 On/Off, 방패만/검/도끼/창/횃불/활/양손 | 각 기능의 독립 허용, YAML false 우선, 무기 피해가 방패 피해에 더해지지 않음 |
| 방어+보조공격, 유지/해제/재입력, 방어 먼저 해제 | 한 번만 돌진하고 무기 보조공격 누출 없음. 방어 없이 시작한 무기 입력을 나중에 돌진으로 바꾸지 않음 |
| 쿨다운·스태미나 부족·파손·공격/회피/경직·과중량 | 금지 상태에서 시작하지 않음. 실패 입력이 무기 공격으로 바뀌지 않음 |
| 키보드/게임패드·토글 방어·인벤토리/메뉴 | 게임 입력 차단 유지, 기존 무기 힌트와 추가 돌진 힌트 공존 |
| 플레이어 둘의 서로 다른 장비, 충전 중 장비/설정/YAML 변경 | 다른 플레이어에게 영향 없이 금지된 충전 초기화. 방패 해제 후 잔여 충전 없음 |
| 호스트·원격 클라이언트·데디케이트·크로스플레이 | 초기 동기화/관리자 잠금, 소유자 반사 1회, 전송 중 정책 변경에도 일반 피해 전달, 중복 이벤트 무시 |
| 기존 방패 단독 공격·투척·돌진 | 자원/쿨다운·반환 동작 유지. 투척 중 사망/접속 해제/인벤토리 만재 시 아이템 소실·복제 없음 |
| 선택적 WarfareTweaks/SecondaryAttacks | Harmony 패치 순서와 공개 연동 컨텍스트 유지 |

클라이언트 DLL과 서버 DLL의 자동 검사 성공만으로 이 실행 확인을 완료했다고 판단하지 않는다.
