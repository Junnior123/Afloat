# Afloat

선택한 Windows 창을 단축키 한 번으로 항상 위에 고정하는 작은 유틸리티입니다.

![Afloat](assets/Afloat-preview.png)

## 주요 기능

- `Ctrl + Alt + Space`로 현재 창 고정 / 해제
- 여러 창을 동시에 고정하고 목록에서 관리
- 창을 왼쪽 위에 작게 배치하고 원래 위치로 복원
- 고정 / 해제 알림과 자연스러운 애니메이션
- 단축키, 알림, 애니메이션, 하드웨어 가속 설정
- Windows 자동 실행과 트레이 시작
- 계정과 인터넷 연결 없이 로컬에서 동작

## 설치

[최신 릴리즈](https://github.com/Junnior123/Afloat/releases/latest)에서 `Afloat-Setup-1.1.0.exe`를 내려받아 실행하세요.

설치 프로그램이 .NET 8 Desktop Runtime을 확인하고 바탕 화면 및 시작 메뉴 바로가기를 선택할 수 있게 해줍니다.

## 사용법

1. 항상 위에 둘 창을 클릭합니다.
2. `Ctrl + Alt + Space`를 누릅니다.
3. 해제하려면 같은 창에서 단축키를 다시 누릅니다.

게임과 함께 사용할 때는 테두리 없는 창 모드를 권장합니다. 관리자 권한 앱이나 독점 전체 화면 위에서는 Windows 정책에 따라 제한될 수 있습니다.

## 요구 사항

- Windows 10/11 x64
- .NET 8 Desktop Runtime x64

## 직접 빌드

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1
```

C#과 WPF로 제작했으며 외부 NuGet 패키지를 사용하지 않습니다.

