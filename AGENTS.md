## 자동 적용: RoBIM 버튼 제작

사용자가 버튼 제작/수정을 요청하면 별도의 문서 링크나 스킬 호출 요청을 기다리지 말고 아래 공용 기준을 읽고 적용한다. 사용자에게 제작 규칙을 다시 설명하도록 요구하지 않는다.

- 이 저장소가 Workspace이면 로컬 START_HERE.md부터 읽는다. 기능 저장소이면 인증된 GitHub 연결 또는 Workspace clone으로 https://github.com/RoBIM-tech/RoBIM-Workspace/blob/main/START_HERE.md 를 읽는다.
- 이어서 Workspace의 AGENTS.md, docs/developer/NEW_BUTTON.md, BUTTON_CONTRACT.md, NAMING.md, BUTTON_DELIVERY.md, REPOSITORY_ACCESS.md 및 .codex/skills/robim-new-button/SKILL.md를 읽는다. 공통 UI/API와 정확한 Libraries 버전, 기능 저장소의 README·전용 스킬도 확인한다.
- 새 버튼은 RoBIM-tech 소유의 독립 저장소·모듈·명령·패키지 하나로 만들고 통합 생성기의 --help를 확인해서 사용한다. 기존 버튼 ID와 사용자 설정을 유지한다.
- 저장소 생성 직후 robim-dev·robim-test를 각각 Read로 반드시 연결하고 robim Read와 실제 제작자 개인의 해당 저장소 Admin을 재조회한다. 팀 소속만으로 자동 연결되지 않는다. 조직 Owner는 신규 조직 저장소에도 Admin이므로 robim-admin 팀 연결은 필수가 아니다.
- 기본 브랜치 robim-package.json, robim-addon 토픽, TEST Release ZIP/SHA-256, Manager 검색 범위를 준비한다. 중앙 등록 PR/Manager 재배포를 신규 발견의 필수 조건으로 요구하지 않는다.
- 코드 작성·게시·병합·Release·설치는 사용자가 요청한 범위에서 수행한다. 이 지침 자체가 계정 권한이나 배포 승인을 부여하지 않는다. 권한/인증이 없으면 필요한 단계만 설명하고 가능한 작업을 계속한다. 비공개 문서를 읽지 못했으면 읽었다고 주장하지 않는다.
- 모든 기능 창의 제목 표시줄에만 `기능명 · v.실행버전 · 채널`을 표시한다. 내부 상단에 같은 기능명·버전을 다시 표시하는 중복 헤더를 만들지 않는다. 기본 제목 표시줄은 부분별 글자 크기를 지원하지 않으므로 동일 크기를 사용한다. 설정·미리보기·결과·진행·오류·확인 창도 포함한다. 공용 UI 버전이나 원격 최신 버전을 실행 버전으로 대신 표시하지 않는다. 기존 버튼은 다음 수정 시 해당 기능의 모든 창을 점검·적용하고 UI_ADOPTION에 결과와 미검증 항목을 기록한다. 상세 기준은 Workspace docs/developer/NAMING.md를 따른다.
- 문서·전용 스킬·UI 적용 기록을 함께 갱신하고 빌드·게시·Manager 표시·설치·Host 적용·실모델 실행 결과를 구분한다. 미수행 검증은 미검증으로 기록한다.

# RoBIM independent feature

- Read README.md and `.codex/skills/robim-workset-manager/SKILL.md`.
- One button/command/package per repository. Create a separate repository for another button. Do not use long-lived button branches.
- New functionality is a native Revit Add-in, not a new pyRevit command.
- Keep module/command IDs stable. Use robim-addon topic, main descriptor and TEST Release for discovery; central catalog registration is optional.
- Start in TEST. Stable needs real host verification, reviewer, host build and HTTPS evidence in robim-release.json.
- Run build.ps1 with CommonFeed; ManagerPath is optional for central-catalog compatibility. Never commit generated DLLs or Autodesk binaries.
- command-only modules must not register long-lived events or startup hooks implicitly. Keep document changes in command Transactions.
