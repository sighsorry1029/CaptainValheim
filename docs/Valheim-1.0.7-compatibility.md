# Valheim 1.0.7 대응 — 2026-09-09

기준 커밋 `f0a0485`(CaptainValheim 1.0.9), 기존 Valheim 0.221.12 → Windows x64 **1.0.7** 대응이다. 대응 릴리스 버전은 1.0.10이다.

## 확인된 실패와 변경

| 근거·호출 경로 | 변경 | 보존·주의 사항 |
| --- | --- | --- |
| 제공 로그의 `ItemDataGetTooltipShieldGuidancePatch` 대상 미발견 → `Plugin.Awake` 중단. 정적 `ItemData.GetTooltip`에 여섯 번째 `bool appending` 추가 | 정확한 6인자 오버로드 지정 | 기존 item/결과 전달과 안내 추가 정책 유지. 인스턴스 오버로드에는 패치하지 않음 |
| 기존 최종 DLL이 구 `EffectList.Create` 5인자와 `SEMan.AddStatusEffect` 4인자를 호출 | 1.0.7 원본 DLL로 재컴파일하여 새 선택 인자의 기본값을 사용 | 기존 이펙트·쿨다운 정책 유지. 게임의 개인별 게임패드 효과 등 새 기능을 임의로 적용하지 않음 |
| 병합 ServerSync의 설정 전송·설정 변경 이벤트에서 `ZRoutedRpc.Everybody`를 `ldsfld`로 읽음. 새 원본은 `const long = 0`; 새 접속 목록 전송도 기존 버퍼 범위를 벗어남 | 검토된 공통 기준본 `valheim-1.0.7-r1`을 vendor. 원본 DLL 재컴파일, 공개 `ZNet.IsAdmin` 사용, 접속 중 PlayerList/HistoricalPlayerList/AdminList/NetTime 순서 보존 | 설정 GUID·관리자 잠금·버전 협상·wire 형식 유지. 다른 모드 DLL과 게임 설치에는 ServerSync를 별도 배치하지 않음 |
| 빌드 참조가 설치 폴더의 publicized DLL을 사용함. 원본에서는 Attack 필드, UI 상태, 일부 메서드가 비공개 | 원본 `assembly_valheim`, `assembly_utils`, `assembly_guiutils` 참조. `GetWeapon`, `LeftItem`, `RightItem` 공개 API 사용. 나머지는 `GameAccess`의 캐시된 typed field/delegate 및 기존 `ProjectileAccess` 사용 | 원본 DLL을 수정하거나 publicize하지 않음. 비공개 Harmony 대상의 `nameof`는 같은 이름의 문자열로 변경. 인터페이스 `IEquipmentVisual.Setup`은 실제 인터페이스 메서드를 통해 호출 |
| 1.0 `HitData.DamageTypes`에 `m_nonPlayer` 추가, 새 `m_variant`가 상태효과 처리에 전달됨. 수동 반사 스냅샷은 이를 누락 | 반사 스냅샷의 피해 직렬화·유한값 검사에 새 채널 추가, projectile hit variant도 송수신·재반영. 자체 방패 HitData 생성 시 게임과 같은 attack eitr/weapon variant 전달 | 기존 계수·타겟·스태미나·내구도 계산은 유지. 원래 반사 정책에서 제외하던 회복/자원 효과를 새로 반사하는 정책은 추가하지 않음 |
| 새 `Projectile`은 `m_hitMidFlight`가 켜진 템플릿에서 `OnHit` 밖의 `DoAOE`로 피해를 줄 수 있음. 모드는 템플릿을 재사용하며 자체 컨트롤러가 피해·연쇄·중복을 처리 | 방패 투척 인스턴스에서 `m_hitMidFlight = false` | 일반/반사 투사체의 게임 정책은 그대로. 실제 템플릿 리소스의 설정값을 덤프한 결과는 아니며 새 코드 경로가 자체 처리와 중첩되지 않도록 한 예방적 대응 |

`GameAccess` 한 파일은 원본 접근 제한을 지키기 위한 경계다. 필드 검색과 delegate 생성은 타입 초기화 때 한 번 수행한다. 공격/Update/FixedUpdate에서 리플렉션 검색·인자 배열을 만들지 않는다. `IEquipmentVisual.Setup`의 reflection 인자 배열은 대체 시각 오브젝트를 생성할 때만 생긴다. 새 매 프레임 전역 검색이나 UI 재생성은 추가하지 않았다. 실제 성능 프로파일링은 하지 않았다.

## 외부 계약과 실행 경계

- **반사 네트워크 형식은 v3로 변경했다.** RPC 이름은 `CaptainValheim_DeliverShieldReflectDamageV3`, 광고 ZDO 키는 기존 `CaptainValheim_ShieldReflectProtocol`을 유지하며 값이 3인 상대에게만 보낸다. 다른 값/미설치 상대는 기존 안전 경로대로 바닐라 피해 전달을 사용한다. v2 패킷을 v3로 읽는 fallback은 없다. 서버와 클라이언트 모두 같은 패치를 사용하는 것이 검증 대상이다.
- `HitData.Serialize/Deserialize`는 1.0.7 게임 구현을 직접 호출한다. 게임 자체 형식도 바뀌었으므로 0.221.x 통신·실행·별도 마이그레이션은 지원하지 않는다. 기존 사용자 YAML/cfg/아이템 저장 파일을 변환하거나 삭제하지 않는다.
- cfg 키, YAML 스키마·동기화 이름, 기본 리소스·번역, 공개 `WarfareTweaksBridge.TryGetShieldHitWeaponPrefabName`, 선택적 WarfareTweaks/SecondaryAttacks 연동을 유지했다. manifest BepInEx 요구 버전만 사용자 지침에 따라 `5.4.2350`으로 수정했다. 설치된 BepInEx 자체를 교체하지는 않았다.
- Harmony Priority/Before/After, `__runOriginal`, Prefix/Postfix/Finalizer의 상태 전달과 정리를 유지했다. `Character.Damage → RPC_Damage → BlockAttack/BlockDamage`의 동일 HitData scope와 sender 검증, owner·대상 ZDO 검증, 이벤트 중복 억제, reflected marker를 유지했다.
- 투척의 1개 차감·보관·회수/드롭 및 `_transferred`/`_dropped`, 초기화/파괴의 이벤트·async 작업 정리를 유지했다. 게임의 `Inventory.AddItem(ItemData)`/`RemoveItem(ItemData,int)`와 `ItemData.Clone`을 계속 사용하므로 새로운 슬롯·아이템 필드를 자체 저장 형식으로 재구현하지 않는다.
- 로컬 플레이어 입력·UI와 서버 설정 권한의 분기를 유지했다. DLL 복사는 로컬 클라이언트 `BepInEx/plugins`에만 수행했다. 데디케이트 서버에 설치하거나 실행하지 않았다.

## 검토 범위와 근거

전역 자료는 `C:/Users/blizz/.codex/references/valheim/INDEX.md` 및 `comparisons/0.221.12--1.0.7-windows-x64/CaptainValheim/`에 있다. 제공 로그는 진단 데이터로만 읽었다.

| 대상 | 0.221.12 원본 | 1.0.7 원본 |
| --- | --- | --- |
| 클라이언트 | `snapshots/client-b21981559-windows-x64-20260909T123305Z` | `snapshots/client-b25185596-windows-x64-20260909T131109Z` |
| 데디케이트 | `snapshots/dedicated-server-b21981590-windows-x64-20260909T123305Z` | `snapshots/dedicated-server-b25185644-windows-x64-20260909T131109Z` |

위 경로는 전역 valheim 자료 폴더 기준이다. 보관 원본은 각 `original`, 추출은 `derived/ilspy-9.1.0.7988-r1/assemblies/<assembly>/csharp`, `metadata.jsonl`, `assembly.il`이다. 기존 동일 도구/설정의 추출·전체 무결성 검증 보고서를 재사용했고 새 원본의 실제 참조 게임 DLL 3개씩은 manifest SHA-256과 다시 대조했다. 설치 클라이언트 assembly_valheim도 목표 스냅샷과 일치했다. 이전 스냅샷은 변경하지 않았다.

- 실제 빌드 대상: `CaptainValheim.csproj`의 명시적 C# 목록, net48 BepInEx 플러그인, ILRepack으로 ServerSync/YamlDotNet 병합. 진입점은 `CaptainValheimPlugin.Awake/Update/OnDestroy`.
- 직접/간접 호출: 최종 병합 DLL의 게임 멤버 참조, Harmony 대상·인자·필드 주입, 명시적 리플렉션 계약을 클라이언트/서버 원본에서 자동 대조했다. ServerSync의 ZNet/ZRpc 핸드셰이크·소켓·관리자 목록 접근도 포함했다.
- 의미 비교 집중 영역: Projectile Setup/OnHit/비행 피해/SpawnOnHit, HitData와 수동 반사 패킷, Attack의 새 hit fields 및 내구도, Humanoid BlockAttack·장착/획득, Inventory의 추가/제거·게임 저장 위임, EffectList, ZNetView/ZRoutedRpc 호출, 툴팁. 참조 메서드 중 변경된 후보 34개 목록도 보관했다. 목록 전체를 동일 깊이로 수동 검토했다는 의미는 아니다.
- 공식 1.0 노트의 인벤토리 확장·아이템·Unity/효과 변경을 검토 후보로 사용했고 API/원본 동작과 연결한 항목만 수정했다. 새 게임 기능이 방패 모드 기능을 대체한다고 판단한 항목은 없다.
- 제외/미검토: 다른 설치 모드의 수정, 외부 라이브러리 전체 소스 감사, 게임 전체 코드·네이티브 Unity/Mono·리소스 내부 객체/애니메이션·새 방패 프리팹 전수 검사, 플랫폼별 실행. 생성물 bin/obj/ZIP과 외부 DLL은 소스 구조 변경 대상에서 제외하되 최종 병합 DLL과 실제 참조는 검증했다.

## 수행한 검증

1. `dotnet build CaptainValheim.csproj -c Debug -p:DeployToGame=true`: **경고 0, 오류 0**, 병합과 로컬 플러그인 복사 성공.
2. 최종 DLL과 로컬 복사본 SHA-256 일치: `CB397FF230CA5EF69EAE8BA7150EDAF270D1EDFB4892CD25C9BCDA28F2BB48E4`.
3. 각 클라이언트/데디케이트 원본에서 **게임 멤버 참조 333개, Harmony 대상 35개, 명시적 리플렉션 계약 26개** 검사 통과. 이 검사는 Harmony 패치를 실제로 설치한 결과와 다르다.
4. 각 원본으로 실제 관리형 코덱 메서드 **36 assertions** 통과: 12개 피해 채널과 패킷 경계, 유한값/NaN/±Infinity, 게임 HitData의 eitr/variant, 반사 variant, v3 왕복, v2 거부, 잘린 패킷 거부. .NET 10 테스트 호스트이며 Unity/게임 실행 결과가 아니다.
5. 기존 1.0.9 Release DLL을 같은 정적 검사에 넣으면 툴팁 대상 1개, 구 메서드 2개, ServerSync 상수 접근 3개가 실패한다. 따라서 검사기가 실제 이전 불일치를 탐지함을 확인했다.
6. ServerSync는 공통 기준본 `valheim-1.0.7-r1`, SHA-256 `B4DD786997F4E90D770F09EF3E9D64154754FE7E8EDFB4841795751895B35846`을 사용한다. 원본 참조 재컴파일, 관리자 공개 API, 접속 메시지 버퍼 검증 범위는 전역 기준본 문서에 기록되어 있다.

재실행 예시(저장소 루트, .NET 10 SDK 필요):

```powershell
dotnet build CaptainValheim.csproj -c Debug -p:DeployToGame=true
dotnet run --project tools/CompatibilityChecks -- bin/Debug/CaptainValheim.dll "C:/Program Files (x86)/Steam/steamapps/common/Valheim/valheim_Data/Managed" "C:/Program Files (x86)/Steam/steamapps/common/Valheim/BepInEx/core" "$env:TEMP/CaptainValheim-client-contracts.json"
```

## 아직 필요한 실제 실행 검증과 리스크

**패치 후 게임을 새로 실행하지 않았다.** 원본의 메타데이터/관리형 코덱 검증은 원본 Unity Mono에서의 delegate 생성·비공개 접근, Harmony detour 설치, 시각/물리/네트워크 실행을 보증하지 않는다. 제공 로그의 CaptainValheim 오류는 패치 전 증거이고 다른 모드 초기화 실패도 섞여 있다.

| 실행 조건 | 확인 사항 |
| --- | --- |
| 클라이언트 시작/월드/재접속 | 초기화 오류 없음, 툴팁·키힌트·도감·언어 변경, 장착/해제와 프레임별 UI 중복 생성 없음 |
| 방패 기본/투척/돌진/블록차지 | 애니메이션·판정·스태미나·내구도·쿨다운; 충돌/반환/수명 만료에 아이템 총합 1개, 인벤토리 가득 참·확장 슬롯·중간 설정 변경 |
| 호스트/원격 클라이언트/데디케이트 | 설정 초기 동기화·관리자 잠금·설정 재적용; 각각의 owner에서 반사 1회, 상태효과 variant, sender 위조·중복 이벤트·미설치/v2 상대의 바닐라 전달 |
| 파괴/소유권 이전/연결 해제 | 투척 중 사망·끊김·owner 변경·장면 전환에서 소실/복제 및 이벤트 잔류 없음. 기존 투척 회수 경로의 소유권 이전은 별도 실제 검증이 필요 |
| 선택적 모드 | WarfareTweaks 공개 컨텍스트와 SecondaryAttacks 패치 순서, 서로 다른 프리팹 정책. 반복 보호 코드나 반사 투사체의 원래 스폰 정책은 통합/제거하지 않음 |

기존 반사 템플릿의 `m_respawnItemOnHit` 같은 아이템 생성 정책은 이번 1.0 변경과 별개의 리소스별 검토 위험이다. 해당 경로를 일반 방패 투척과 일괄 통합하지 않았다. 테스트 월드에서 실제 사용 템플릿별 아이템 수량을 확인해야 한다.

## 되돌릴 수 있는 적용 단위

1. 원본 참조 + 캐시 접근자 + 정확한 Harmony 대상: 별도 Debug 빌드/정적 계약 검사.
2. 공통 ServerSync 기준본 적용: 고정 입력 해시와 최종 병합 IL 검사 후 설정 동기화·접속 목록 순서 실행 검사.
3. 1.0 피해/variant 반사 패킷 v3와 방패 비행 피해 억제: 코덱 검사 후 멀티플레이·수량 검사. 코덱/버전/송수신 변경은 한 단위로 되돌린다.
4. 위 실제 실행 시나리오 확인 후 별도 릴리스 요청에서 모드 버전/릴리스 노트/Release 패키지를 만든다. 이번 Debug의 모드 버전이 기존 배포판과 같으므로 멀티플레이 검증은 버전 표시만 믿지 말고 같은 빌드 해시를 사용한다.
