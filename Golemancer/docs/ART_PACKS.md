# 이미지 객체팩과 애니메이션 시트

게임은 객체팩에 들어 있는 실제 SVG/PNG 파일을 읽는다. 타일셋, 정적 그림, 캐릭터 애니메이션은 서로 다른 팩이다. 게임 코드가 타일 그림을 생성하거나 정지 캐릭터 그림을 주기적으로 위아래로 흔들지 않는다. 화면의 선택 표시·공격 예고·진행 막대는 UI다.

## 설치와 교체

이미지는 저장소 루트의 `Content/Packs/<팩>/Images`에 둔다. `Builds/Windows/Golemancer.exe`도 이 경로를 사용하므로 실행 파일을 갱신할 때 다시 복사하지 않는다. 이미지팩 v2를 쓰는 경우 최초 한 번만 저장소 루트에 풀면 된다. 이미지에 Git 제외 규칙을 적용하지 않으므로 사용자 Git에서 직접 추가·푸시할 수 있다. 변경한 이미지는 게임을 다시 시작하면 읽는다. 경로는 XML 파일을 포함하는 팩 폴더 기준이며 다른 팩의 디렉터리로 벗어날 수 없다.

| 폴더 | ID | 내용 |
|---|---|---|
| `05.FeastTrailTiles` | `feast_trail_tiles` | 지형 이미지, 타일 종류·통행 여부 |
| `06.FeastTrailArt` | `feast_trail_art` | 정적 SVG, 시설·자원·아이콘·초상화 |
| `07.FeastTrailAnimations` | `feast_trail_animations` | 투명 PNG 12장과 애니메이션 정의 |
| `91.DeguldolArt` | `deguldol_art` | 데굴돌 전용 시트 24프레임과 돌 아이콘 |

PNG는 투명 RGBA를 권장한다. SVG는 path, rect, ellipse, circle, line, polygon, polyline, group, text, linearGradient와 기본 변환을 지원한다. 필터·마스크·외부 이미지·스크립트가 들어간 복잡한 SVG는 PNG로 내보낸다. 이미지팩이 누락되거나 프레임이 시트 밖을 가리키면 시작 시 오류에 경로 또는 원인을 표시한다.

## 타일 시트를 별도 팩으로 넣기

`Content/Packs/95.MyTiles/pack.xml`:

```xml
<ObjectPack id="my_tiles" version="1.0.0" contracts="2">
  <Depends id="feast_trail_tiles" minVersion="1.0.0" />
  <Data path="tiles.xml" />
</ObjectPack>
```

`tiles.xml`과 `Images/terrain.png`를 같은 팩에 넣는다. 다음 예는 PNG 한 장에서 64×64 영역 두 개를 사용한다.

```xml
<GameContent>
  <Tilesets>
    <Tileset id="feast_trail">
      <Tile id="grass" image="Images/terrain.png"
            x="0" y="0" width="64" height="64" walkable="true" />
      <Tile id="water" image="Images/terrain.png"
            x="64" y="0" width="64" height="64" walkable="false" />
    </Tileset>
  </Tilesets>
</GameContent>
```

영역을 생략하면 이미지 전체가 타일 하나다. `Tile` 하나는 같은 ID의 기존 정의를 교체하고 나머지 타일은 유지한다. `walkable`은 실제 이동 판정에 사용된다. 새 타일셋은 새 ID를 만들고 맵에 `<Map ... tileset="my_tileset">`으로 연결한다. 이미 저장된 맵은 저장된 TilesetId를 사용한다.

영역 캐시와 선택적 `Terrain` 설정으로 풀 무리·흙 알갱이·양쪽의 경계 조건을 적용한다. 위에 덮는 레이어와 카펫의 실/안쪽 마감 이미지는 [TERRAIN_PACKS.md](TERRAIN_PACKS.md)를 참고한다. 실제 기본/마감 이미지를 그대로 샘플링하며 DLL은 표시 비율만 계산한다.

## 애니메이션별 시트와 오프셋

`Content/Packs/96.MyCharacter/pack.xml`:

```xml
<ObjectPack id="my_character_art" version="1.0.0" contracts="2">
  <Depends id="feast_trail_animations" minVersion="1.0.0" />
  <Data path="animations.xml" />
</ObjectPack>
```

`animations.xml`:

```xml
<GameContent>
  <Sprites>
    <Sprite id="harvest_golem">
      <Animation state="move" image="Images/walk.png"
                 frameWidth="96" frameHeight="96" columns="4" frames="8"
                 x="0" y="0" frameSeconds="0.10" loop="true"
                 drawWidth="1.4" drawHeight="1.4"
                 pivotX="0.5" pivotY="0.9" offsetX="0" offsetY="0" />
      <Animation state="work" image="Images/harvest.png"
                 frameWidth="128" frameHeight="96" columns="4" frames="4"
                 frameSeconds="0.12" loop="true"
                 drawWidth="1.8" drawHeight="1.35"
                 pivotX="0.5" pivotY="0.9" offsetX="0.15" offsetY="-0.08" />
    </Sprite>
  </Sprites>
</GameContent>
```

이 팩은 수확 골렘의 이동·작업 애니메이션만 교체하고 대기·공격 등은 유지한다. Animation 하나를 교체할 때는 해당 상태의 필요한 속성을 모두 작성한다. 원하는 여러 상태가 같은 이미지 경로를 사용할 수도 있다.

| 속성 | 단위와 의미 |
|---|---|
| `image` | 팩 기준 시트 파일 경로 |
| `frameWidth`, `frameHeight` | 프레임의 기준 픽셀 크기 |
| `columns`, `frames` | 행의 열 수, 재생할 프레임 수; 좌→우, 위→아래 |
| `x`, `y` | 첫 프레임의 시트 내 픽셀 좌표 |
| `frameSeconds` | 프레임 하나의 표시 시간(초) |
| `loop` | true: 반복, false: 마지막 프레임 유지 |
| `drawWidth`, `drawHeight` | 기준 프레임의 화면 크기(타일 단위) |
| `pivotX`, `pivotY` | 그림 내부 기준점의 비율; 0.5, 1은 아래쪽 중앙 |
| `offsetX`, `offsetY` | 기준점에 더하는 타일 단위 보정; +X 오른쪽, +Y 아래쪽 |

월드 기준점은 객체 점유 영역의 `X + Width × 0.5`, `Y + Height × 0.86`이다. 여기에 애니메이션 오프셋을 더하고 그림의 pivot을 맞춘다. 오프셋은 시각적 위치만 바꾸며 이동 목적지·충돌·사거리·저장된 정수 좌표를 바꾸지 않는다. 확대·축소에도 같은 타일 비율로 유지된다.

기본 상태는 `idle`, `move`, `work`, `attack`, `hit`, `death`다. 요청된 상태가 없으면 `idle`을 사용한다. `idle`도 별도 프레임을 재생한다. 단일 정적 이미지가 필요한 시설은 frame 크기를 생략한 1프레임 `idle`을 쓸 수 있다. 기본 제공 시트는 정면 중심이며 방향별 보행 시트는 포함하지 않는다.

## 일정하지 않은 프레임 영역

여백이나 포즈 크기가 다르면 Animation 안에 실제 영역을 프레임 순서대로 지정한다. Frame 개수는 `frames`와 같아야 한다. 이 경우 `x/y/columns`로 계산한 격자 대신 Frame의 영역과 pivot을 쓴다.

```xml
<Animation state="attack" image="Images/attack.png"
           frameWidth="128" frameHeight="128" columns="2" frames="2"
           frameSeconds="0.15" loop="false"
           drawWidth="1.6" drawHeight="1.6" offsetX="-0.1" offsetY="0">
  <Frame x="8" y="10" width="105" height="114" pivotX="0.5" pivotY="0.98" />
  <Frame x="136" y="6" width="118" height="120" pivotX="0.42" pivotY="0.98" />
</Animation>
```

개별 Frame의 표시 크기는 `drawWidth × Frame.width / frameWidth`, `drawHeight × Frame.height / frameHeight`다. 잘라낸 크기가 다른 프레임을 같은 사각형에 강제로 늘리지 않는다.

제공 PNG는 각각 1024×1536이며 대기·이동·작업·공격·피격·쓰러짐 순으로 네 포즈씩 배치했다. 그림의 여백이 일정하지 않아 XML에 명시적인 Frame 영역과 발 기준점을 제공한다. 단순히 256×256 격자로 다시 자르지 말고 이 메타데이터를 함께 사용한다.

## 제작과 확인

캐릭터 PNG는 이미지 생성 도구로 만든 실제 그림이다. 정적 SVG는 이전 버전에서 작성한 벡터 파일을 재사용했다. 엔린의 에메랄드 팔찌·머리 위 미니골렘·감정 칠판을 유지했다.

제작 지시의 공통 규격은 투명 배경, 기존 캐릭터의 실루엣·색·소품 유지, 4열×6상태, 상태별 4개의 다른 포즈, 이웃 프레임에 겹치지 않는 충분한 여백이었다. 제작 골렘과 샘물의 왕은 프레임 간 겹침을 수정한 시트가 최종본이다. 원본 픽셀은 프레임 추출 과정에서 재작성하지 않는다.

- `python tools/fit-animation-frames.py`: 제공된 1024×1536 시트의 투명 여백을 찾아 Frame 메타데이터를 작성한다. 그림이 겹치면 실패하며 자동으로 그림을 잘라 고치지 않는다. 임의 모드 시트의 일반 변환기로 쓰지 않는다.
- `python tools/verify-art.py`: 파일 존재, 투명도, 프레임 경계, 빈 프레임, 서로 다른 프레임을 검사하고 실제 XML 영역으로 미리보기를 만든다.
- `node tools/export-art.mjs`: 기존 정적 SVG 원본을 별도 팩 폴더로 내보내는 선택적 제작 도구다. PNG 애니메이션을 생성하지 않는다.

제작 도구의 Python 의존성은 Pillow·numpy, SVG 내보내기는 Node.js다. 게임 실행에는 필요하지 않다. 제공 시트와 XML은 이미지팩 ZIP에 함께 들어 있으며 `Previews/Animation-Preview.gif`로 포즈를 확인할 수 있다.
