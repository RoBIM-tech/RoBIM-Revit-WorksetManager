# 창 제목·헤더 적용 현황

| 창 | 제목 표시줄 | 버전 출처 | 검증 |
| --- | --- | --- | --- |
| 시작 예제 TaskDialog | 기능명 · v.실행버전 · 채널, 내부 중복 제목 없음 | 실행 Command 어셈블리 InformationalVersion | 생성 코드만 제공, SDK 빌드·호스트 화면 미검증 |

새 창 및 보조 창을 추가하면 행을 추가한다. 기존 기능 수정 시 해당 기능 전체 창을 점검한다.
버전 확인 실패는 버전 미확인, 검증되지 않은 정식 채널은 채널 미확인으로 표시한다.
예제는 ReleaseMetadata.Resolve(null, ...)를 사용하므로 실사용 승격 시 실제 로드한 모듈의 검증된 배포 상태를 연결해야 한다.
[전체 제목 기준](https://github.com/RoBIM-tech/RoBIM-Workspace/blob/main/docs/developer/NAMING.md)을 따른다.
