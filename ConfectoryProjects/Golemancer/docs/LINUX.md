# Linux 네이티브 호스트

SDL2 창에 공통 Skia 화면을 표시한다. 브라우저를 사용하지 않는다. 제공하는 linux-x64 패키지는 .NET 런타임·실제 이미지·XML·독립 객체팩 DLL을 포함한다. 사용자 저장 파일은 포함하지 않는다.

## 다운로드 패키지 실행

압축을 푼 뒤 `Golemancer` 폴더에서 실행한다.

```sh
../../ConfectoryEngine/start.sh "$PWD" linux
```

운영체제에 SDL2, fontconfig와 한글 글꼴이 필요하다. Debian/Ubuntu 계열 예시는 다음과 같다. 배포판별 패키지 이름은 다를 수 있다.

```sh
sudo apt install libsdl2-2.0-0 libfontconfig1 fonts-noto-cjk
```

키보드 WASD/방향키, Tab 모드 전환, E 줍기, R 녹화, T 반복, B 건설, I 장비, Space 구르기, 1/2 회복을 지원한다. 좌클릭은 빠른 사용/이동, 우클릭은 버블, 휠은 줌이다. Shift는 예약이며 게임패드 매핑은 Android와 같은 논리 액션을 쓴다. 수량창은 숫자 입력·슬라이더·화면 버튼을 사용한다. `--touch`는 공통 가상 스틱·버튼을 표시하는 비교용 옵션이다. SDL 멀티터치 장치 연결은 이번 호스트에 구현하지 않았다.

저장은 기본적으로 `$XDG_DATA_HOME/Golemancer/Saves`, 미설정 시 `~/.local/share/Golemancer/Saves`에 둔다. `--saves /path`로 바꿀 수 있다. 창 포커스를 잃으면 입력을 정리하고 세션을 멈추며 자동 저장한다.

## 소스 빌드

.NET 10 SDK에서 다음 명령을 실행한다. 선택한 엔진에 이 프로젝트 경로를 전달해서 빌드한다.

```sh
../../ConfectoryEngine/build.sh "$PWD" linux
../../ConfectoryEngine/start.sh "$PWD" linux
```

`CONFECTORY_DOTNET`으로 dotnet 경로를 지정할 수 있다. arm64는 `../../ConfectoryEngine/build.sh "$PWD" linux-arm64`로 빌드하도록 준비했지만 이번에 빌드·실행 검증한 CPU는 x64다. self-contained 출력은 `Builds/Linux/<RID>`에 생기며, Content는 게임 폴더 루트에 유지한다.

```sh
python3 tools/package-linux.py --output /path/Golemancer_Linux_x64.tar.gz --source <source-commit>
```

이 도구는 런타임과 실제 Content를 묶고 SHA256을 기록한다. 전체 Linux 런타임은 별도 다운로드로 제공하고 Git에는 소스·스크립트·빌드 메타데이터를 기록한다.

## 검증

```sh
../../ConfectoryEngine/start.sh "$PWD" linux --smoke --frames 90 --screenshot /tmp/golemancer-linux.png
```

`PRESENTATION_SMOKE_PASS`와 `LINUX_SMOKE_PASS`를 확인한다. 독립 DLL 적재, 아트, 저장, 입력 분리, 카메라, 버블·수량창과 실제 SDL 이벤트 큐를 검사한다. 화면 없는 환경에서는 `SDL_VIDEODRIVER=offscreen`을 앞에 붙일 수 있다. 이번 제작 환경의 실행 검증은 이 방식이었으며 물리 화면, 데스크톱 통합, 게임패드 장치와 GPU/FPS 검증은 아니다.
