# 롯데월드 VARCO 3D 모델 넣는 곳

이 폴더에 아래 이름으로 GLB(또는 fbx/obj)를 넣고
**Tools ▸ Map ▸ ★ 롯데월드 VARCO 모델 적용** 을 실행하면 그레이박스가 진짜 모델로 바뀝니다.
(배경 소품을 적용했다면 **Tools ▸ Map ▸ ★ 롯데월드 맵 생성** 을 한 번 더 실행해 배경에 반영)

없는 파일은 건너뛰므로 **부분 적용 가능** — 하나씩 뽑아 넣어도 됩니다.

## 파츠 (건축 블록 — footprint에 맞춰 자동 스케일)

| 파일 이름 | VARCO 생성 프롬프트 예시 |
|---|---|
| `롯데_성기반.glb` | cartoon castle stone base platform, wide square foundation with brick trim, ivory white, stylized lowpoly game asset |
| `롯데_성본체.glb` | cartoon fairy tale castle keep, cube body with arched windows, ivory walls, stylized lowpoly |
| `롯데_성상단.glb` | castle upper parapet with battlements, square, ivory stone, stylized lowpoly |
| `롯데_중앙첨탑.glb` | tall blue cone spire tower of fairy tale castle, stylized lowpoly |
| `롯데_코너타워.glb` | slim cylindrical castle corner tower, ivory stone with small windows, stylized lowpoly |
| `롯데_타워지붕.glb` | blue cone roof for castle tower, fairy tale style, stylized lowpoly |
| `롯데_정문게이트.glb` | castle front gate with golden arch door, fairy tale style, stylized lowpoly |
| `롯데_깃발.glb` | red flag on a golden flagpole, castle top ornament, stylized lowpoly |

## 기믹 · 배경 소품

| 파일 이름 | 쓰임 | 프롬프트 예시 |
|---|---|---|
| `롯데_퍼레이드카.glb` | 퍼레이드 카 (기믹 비주얼) | colorful amusement park parade float car with balloons and star decorations, cute, stylized lowpoly |
| `롯데_롯데월드타워.glb` | 북동쪽 원경 랜드마크 | supertall skyscraper tower, tapered glass curtain wall, Lotte World Tower style, stylized lowpoly |
| `롯데_자이로드롭.glb` | 섬 동쪽 놀이기구 | gyro drop tower ride, tall pole with ring gondola, amusement park, stylized lowpoly |
| `롯데_회전목마.glb` | 섬 서쪽 놀이기구 | carousel merry-go-round with ornate roof and horses, amusement park, stylized lowpoly |
| `롯데_대관람차.glb` | 서쪽 원경 | ferris wheel, amusement park, colorful cabins, stylized lowpoly |
| `롯데_풍선.glb` | 호수 상공 열기구(4개 배치) | hot air balloon ride, red and white striped canopy, small basket, stylized lowpoly |

## 텍스처(선택)

`텍스처_잔디섬.png` / `텍스처_광장바닥.png` / `텍스처_퍼레이드길.png` 을 넣으면 지형 머티리얼에 타일링 적용.

## VARCO 3D 사용 메모

- https://3d.varco.ai 로그인(Google) → 커스텀 워크플로 열기 → Text to 3D 노드로 위 프롬프트 입력.
- 워크플로를 브라우저에 열어두면 Claude가 MCP(varco-3d)로 노드 생성/실행/다운로드를 대신할 수 있음.
- 내보내기는 GLB 권장, "pivot to bottom" 옵션이 있으면 켜기(없어도 적용 툴이 보정함).
