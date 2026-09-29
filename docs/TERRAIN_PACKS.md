# 영역 지형 렌더링과 레이어 팩

그림은 외부 SVG/PNG를 사용한다. 지형 DLL은 그림을 만들지 않고 **어떤 이미지가 얼마나 보일지** 결정하는 마스크만 계산한다. 타일의 충돌·건설·이동 판정은 `TileMap.Tiles`와 `TileDef.Walkable`을 그대로 사용한다.

## 영역 캐시

Windows 호스트는 8×8칸을 한 이미지로 합성하고, 화면에 보이는 영역 이미지만 그린다. 골렘·효과·선택 표시·밤 색조는 그 뒤에 별도로 그리므로 이들의 변화가 바닥을 다시 합성하지 않는다. 기본 64×40 맵 전체는 2,560개 타일 호출 대신 40개 영역 호출이 된다. 실제 호출 수는 뷰포트에 따라 더 적다.

처음 보는 영역이나 바뀐 영역에는 같은 외부 이미지와 규칙으로 만든 저해상도 미리보기를 즉시 표시한다. 두 작업자가 분리된 스냅샷으로 정밀 합성하고, 완료된 결과를 화면 스레드에서 교체한다. 맵·레이어·시드가 바뀌면 오래된 작업을 폐기한다. 시뮬레이션 상태나 WPF 객체를 작업자에게 넘기지 않는다.

정밀 이미지 해상도는 타일당 64픽셀이고, 축소 배율에서는 32픽셀이다. 기본 캐시 상한은 각각 128/256영역이며 화면에서 멀어진 영역부터 제거한다. 현재 맵 전체의 64픽셀 BGRA 버퍼는 약 40.3 MiB다. WPF/GPU의 이미지 복사와 텍스처 메모리는 별도다.

영역 주변 3칸의 스냅샷을 비교해 직접 배열을 바꾸는 기존 모드도 감지한다. 수정된 칸과 번짐에 영향받는 주변 영역만 다시 합성한다. 이미지나 XML/DLL 변경은 기존 팩과 마찬가지로 게임을 재시작해서 읽는다. 월드 좌표 기반 UV·노이즈와 실제 이웃 픽셀을 담는 가장자리 여백 덕분에 카메라·영역 경계가 무늬를 바꾸지 않는다.

## 내보내는 규칙과 받는 규칙

`05.FeastTrailTiles/tileset.xml`이 기본 예제다. 기존의 `image`, atlas `x/y/width/height`, `walkable`은 유지하고, 선택적인 `Terrain`을 추가한다. `Terrain`이 없는 옛 팩은 정확한 타일 경계를 사용한다.

```xml
<Tile id="my_grass" image="Images/grass.png" walkable="true">
  <Terrain tags="vegetation" repeatX="2" repeatY="2">
    <Blend handler="terrain.grass" width=".30">
      <Param key="clump" value=".12" />
    </Blend>
    <Receive distance=".30" strength="1" />
  </Terrain>
</Tile>
<Tile id="my_floor" image="Images/floor.png" walkable="true">
  <Terrain tags="floor">
    <Blend handler="terrain.smooth" width=".025" />
    <Receive distance=".025" strength=".08" />
  </Terrain>
</Tile>
```

`repeatX/Y`는 이미지 전체가 반복되는 월드 크기(칸 단위, 기본 1)다. 큰 베이스 이미지를 여러 칸에 걸쳐 사용할 수 있다. UV 원점은 월드 (0,0)이다.

| 설정 | 의미 |
|---|---|
| `Blend.handler` | 외부 DLL에 등록된 마스크 ID. 생략하면 바깥으로 번지지 않음 |
| `Blend.width` | 자신의 영역 밖으로 나가는 최대 거리, 0~2칸 |
| `Receive.distance` | 같은 층의 다른 지형이 들어올 수 있는 최대 거리, 기본 2칸 |
| `Receive.strength` | 들어오는 마스크의 최대 세기, 0~1, 기본 1 |
| `Receive.rejectTags` | 완전히 차단할 원본 지형 태그, 쉼표로 구분 |
| `Terrain.tags` | 위 규칙에서 판별할 지형 태그 |

같은 층 안에서 본인 칸의 가중치는 1이다. 이웃 가중치는 원본의 마스크에 받는 쪽의 거리 제한과 세기를 적용한다. 색은 가중치를 정규화해 혼합한다. 따라서 칸의 종류가 바뀌는 선에서 두 이미지의 위치가 서로 뒤집히지 않는다. 연속된 같은 지형의 내부 칸 경계에는 마스크를 만들지 않는다. 태그 차단·거리 제한은 **같은 층의 경계**에만 적용하고, 위에 놓인 카펫을 막지 않는다.

`04.TerrainRules` 팩에 들어 있는 기본 DLL 규칙:

| ID | 형태 | 선택적 Param |
|---|---|---|
| `terrain.smooth` | 매끄러운 감쇠 | 없음 |
| `terrain.grass` | 빈 틈이 생기는 불규칙한 풀 무리 | `clump` (기본 .12칸) |
| `terrain.scatter` | 안쪽의 부드러운 감쇠 + 바깥쪽 흩어진 알갱이 | `grain` (기본 .055칸) |
| `terrain.fringe` | 경계 바깥 방향으로 길이가 다른 실 가닥 | `spacing` (.07칸), `thickness` (.22, 간격 대비 비율) |

이 규칙을 참조하는 팩은 `<Depends id="terrain_rules" minVersion="1.0.0" />`를 선언한다. 미등록 handler나 잘못된 숫자는 쿠킹 오류로 보고한다.

## 카펫과 안쪽 마감

카펫도 실제 기본 이미지가 필요하다. 안쪽 마감은 별도 외부 이미지를 안쪽 띠에만 합성한다. 아래 경로의 이미지는 모드 제작자가 제공해야 한다. 기본 게임에 임의의 카펫 그림이나 가구를 추가하지 않는다.

```xml
<Tile id="my_carpet" image="Images/carpet.png" walkable="true">
  <Terrain tags="fabric" repeatX="2" repeatY="2">
    <Blend handler="terrain.fringe" width=".15">
      <Param key="spacing" value=".07" />
      <Param key="thickness" value=".22" />
    </Blend>
    <Receive distance=".015" strength=".1" />
    <Finish image="Images/carpet_hem.png" width=".05" inset=".035" />
  </Terrain>
</Tile>
```

`Finish.width`는 마감 띠의 폭(최대 1칸), `inset`은 칠한 영역의 경계에서 안쪽으로 들어간 거리(최대 1칸)다. 마감 이미지는 기본 이미지와 같은 월드 UV로 반복된다. 이웃한 카펫 칸 사이에는 띠가 생기지 않으며 바깥 윤곽과 구멍의 안쪽에만 생긴다. 실 가닥은 카펫 기본 이미지의 색을 사용한다. 투명한 기본/마감 이미지도 premultiplied alpha로 합성한다.

## 맵의 여러 층

기본 `Rows`는 충돌 지형이다. 그 위에 선택적인 `Layers`를 둔다. 각 레이어 행의 크기는 기본 맵과 같고, `.`은 비어 있는 곳이다. 다른 글자는 기본 `Legend`를 상속하거나 레이어 안에서 재정의한다.

```xml
<Map id="my_room" tileset="my_tiles" width="4" height="3">
  <Legend><Tile char="f" type="my_floor" /></Legend>
  <Rows><Row>ffff</Row><Row>ffff</Row><Row>ffff</Row></Rows>
  <Layers>
    <Layer id="rugs" order="10">
      <Legend><Tile char="r" type="my_carpet" /></Legend>
      <Rows><Row>....</Row><Row>.rr.</Row><Row>....</Row></Rows>
    </Layer>
  </Layers>
</Map>
```

`order`가 작은 층부터 그린다. 같은 값이면 선언 순서를 따른다. `tileset`을 생략하면 기본 맵의 타일셋을 쓰고, 지정하면 다른 타일셋 팩의 재질도 사용할 수 있다. `visible="false"`로 숨길 수 있다. 기본 지형 → 각 시각 레이어 → 골렘/시설물 순으로 그린다. 시각 레이어는 지형 데칼이며 별도 충돌이나 객체의 앞뒤 정렬을 만들지 않는다.

모듈은 `TileMap.Layers`에 맵 크기의 `TerrainMapLayer`를 추가하고 `SetLayer(id,x,y,tileId)`로 칠하거나 빈 문자열로 지운다. 월드를 복사할 때 `TileMap.Clone()`을 사용하면 레이어 배열까지 복제된다. 레이어, 순서, 타일셋, 숨김 상태, 알 수 없는 추가 JSON 필드는 저장된다. 팩을 잠시 제거해도 저장된 미등록 지형/레이어를 지우지 않는다.

## 새 DLL 규칙

`IModuleRegistry`의 기존 계약은 바꾸지 않았다. 새 모듈은 선택적 `ITerrainRegistry`로 자기 규칙을 등록한다. `modules/Golemancer.Terrain`은 Contracts만 참조하는 실제 예제다.

```csharp
public void Register(IModuleRegistry registry)
{
    if (registry is ITerrainRegistry terrain)
        terrain.TerrainBlend("my.snow", new SnowMask());
}
```

`ITerrainBlendRule.Coverage(TerrainSample,TerrainBlendDef)`는 0~1을 반환한다. 입력은 월드 X/Y, 원본 영역까지의 바깥 거리, 가장 가까운 경계의 바깥 법선, 픽셀 크기, 안정적인 시드다. 내부 가중치 1은 렌더러가 처리한다. 설정의 `Width` 밖은 호출하지 않으며 `Parameters`는 XML `Param`을 전달한다. 규칙은 여러 작업자에서 동시에 호출될 수 있으므로 순수하고 결정적이어야 한다. 시뮬레이션 접근·파일 I/O·공유 난수·내부 상태 변경은 금지한다. 재질과 텍스처는 시작 시 고정되고 플레이 중 핫 리로드하지 않는다.

## 검증 범위

`Golemancer.Verification --terrain`은 독립 DLL 적재, 두 방향 경계, 카펫의 실/마감/층 순서, 저장 보존, 부분 무효화, 비동기 취소/교체, 영역 이음새의 픽셀 일치 및 캐시 비용을 검사한다. `--smoke`의 Windows 검사에는 실제 WPF 이미지 디코딩·영역 표시·국소 갱신·축소 배율 전환도 포함된다. Linux의 공유 렌더러 수치는 Windows의 FPS 측정값이 아니다.
