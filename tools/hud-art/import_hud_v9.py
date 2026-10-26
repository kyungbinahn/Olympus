"""HUD v9 아트 반입 (2026-10-26) — D:\\OlympusArt\\ui\\out\\{frames,icons}\\final → Assets/Art/UI/{Skin,Icons}.

아트 납품 프레임은 원본 해상도(900~1170px)다. 그대로 넣으면 40px 칸에 1000px 그림을 줄여 그려서 금선이
지글거리고(바탕 조각은 밉맵을 끈다 — UiIconPostprocessor) 텍스처만 수 MB가 된다. 그래서 화면 크기의 2배로
미리 줄여 넣는다(HUD는 pixelsPerUnitMultiplier로 2배 그림을 1배 두께로 쓴다).

줄이는 크기와 9-slice 테두리는 아트 시안 조립기(OlympusArt/ui/compose_v7.py)의 배치 값에서 나온다:
  칩      높이 40, 양 끝 반원 고정(가로 3-slice)        → 높이 80, 좌우 테두리 44
  받기    176×72, 양 끝 반원 고정                        → 높이 144, 좌우 테두리 76
  탭 타일 원본 테두리 110 → 화면 15~22                    → 테두리 44
  패널    원본 테두리 90 → 화면 22                         → 테두리 44
  원형 버튼 지름 92, Lv 배지 136                           → 184, 272 (늘리지 않으므로 테두리 없음)
테두리 값은 UiIconPostprocessor.SkinBorders와 같아야 한다 — 이 표를 바꾸면 그쪽도 바꾼다.

전선 띠 바탕(금테 + 짙은 갈색)은 아트 납품이 아니라 시안 조립기가 직접 그리는 모양이라 여기서 그린다.

usage: python tools/hud-art/import_hud_v9.py   (D:\\Olympus에서)

⚠️ 돌린 뒤 Skin 폴더를 다시 가져온다(브리지: call "Olympus.Editor.BridgeActions.Reimport Assets/Art/UI/Skin/<이름>.png").
   Unity가 켜져 있으면 새 PNG를 UiIconPostprocessor 표가 컴파일되기 전에 먼저 가져가서 9-slice 테두리가 0으로 박힌다
   (2026-10-26에 전선 띠 끝이 뾰족하게 나와서 찾았다). 테두리가 0이면 HUD 빌더가 늘림 대신 통째로 늘려 그린다.
"""
import os
import shutil
from PIL import Image, ImageDraw

ART = "D:/OlympusArt/ui/out"
SKIN = "Assets/Art/UI/Skin"
ICONS = "Assets/Art/UI/Icons"
SS = 4

# name: (원본, 목표 높이 px, 9-slice 테두리 (L,B,R,T) — UiIconPostprocessor와 같은 값)
FRAMES = {
    "frame_chip": ("frame_chip", 80, (44, 0, 44, 0)),
    "btn_green": ("btn_green", 144, (76, 0, 76, 0)),
    "frame_tab": ("frame_tab", 360, (44, 44, 44, 44)),
    "frame_panel": ("frame_panel", 217, (44, 44, 44, 44)),
    "frame_round_btn": ("frame_round_btn", 184, None),
    "frame_badge_lv": ("frame_badge_lv", 272, None),
}

# 3D 통일 아이콘(형님 선택 2026-10-26: settings-2, mail-1, offer-1, prosperity-3, outpost-2, quest-2).
# 설정·우편·특가·임무는 v6 때 페인팅 시안에서 잘라 넣었던 것을 같은 이름으로 갈아끼운다.
ICON_NAMES = ["icon_settings", "icon_mail", "icon_offer", "icon_quest", "icon_prosperity", "icon_outpost"]

GOLD = (212, 168, 92)
FRONT_FILL = (62, 44, 32)


def front_strip():
    """전선 띠 바탕 — compose_v7.py: 금 둥근 사각(반경 12) 안에 4px 들여 짙은 갈색(반경 9). 2배로 그린다."""
    w = h = 96
    im = Image.new("RGBA", (w * SS, h * SS), (0, 0, 0, 0))
    d = ImageDraw.Draw(im)
    d.rounded_rectangle((0, 0, w * SS - 1, h * SS - 1), 24 * SS, fill=GOLD + (255,))
    d.rounded_rectangle((2 * SS, 2 * SS, w * SS - 1 - 2 * SS, 5 * SS), 6 * SS, fill=(246, 214, 150, 255))
    d.rounded_rectangle((8 * SS, 8 * SS, w * SS - 1 - 8 * SS, h * SS - 1 - 8 * SS), 18 * SS, fill=FRONT_FILL + (255,))
    return im.resize((w, h), Image.LANCZOS)


def circle_mask():
    """월계관 원판 안 프로필 사진을 동그랗게 자르는 마스크(흰 원, 2배 해상도 = 지름 80 → 160)."""
    n = 160
    im = Image.new("RGBA", (n * SS, n * SS), (0, 0, 0, 0))
    ImageDraw.Draw(im).ellipse((0, 0, n * SS - 1, n * SS - 1), fill=(255, 255, 255, 255))
    return im.resize((n, n), Image.LANCZOS)


def badge_disc(im):
    """Lv 글자를 얹을 대리석 원판의 중심·반지름 — 밝고 채도 낮은 픽셀 중 가운데 덩어리."""
    w, h = im.size
    px = im.load()
    xs, ys = [], []
    for y in range(h // 6, h * 5 // 6):
        for x in range(w // 6, w * 5 // 6):
            r, g, b, a = px[x, y]
            if a > 200 and min(r, g, b) > 190 and max(r, g, b) - min(r, g, b) < 40:
                xs.append(x)
                ys.append(y)
    return (min(xs) + max(xs)) / 2, (min(ys) + max(ys)) / 2, (max(xs) - min(xs)) / 2


def main():
    os.makedirs(SKIN, exist_ok=True)
    for name, (src, height, border) in FRAMES.items():
        im = Image.open(f"{ART}/frames/final/{src}.png").convert("RGBA")
        width = round(im.width * height / im.height)
        im = im.resize((width, height), Image.LANCZOS)
        im.save(f"{SKIN}/{name}.png")
        print(f"{name:16s} {im.size}  border={border}")
        if name == "frame_badge_lv":
            cx, cy, r = badge_disc(im)
            print(f"  원판 중심 ({cx:.0f},{cy:.0f}) 반지름 {r:.0f} / {im.size}")

    strip = front_strip()
    strip.save(f"{SKIN}/strip_front.png")
    print(f"{'strip_front':16s} {strip.size}  border=(32,32,32,32)")

    circle_mask().save(f"{SKIN}/circle_mask.png")
    print(f"{'circle_mask':16s} (160, 160)")

    for n in ICON_NAMES:
        shutil.copyfile(f"{ART}/icons/final/{n}.png", f"{ICONS}/{n}.png")
        print("icon", n)


if __name__ == "__main__":
    main()
