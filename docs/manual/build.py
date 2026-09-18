"""index.html -> 설명서.pdf

1. index.html에 쓰인 글자만 모아 SUITE 폰트를 서브셋 (fonts/SUITE-<weight>.ttf)
2. Chrome 헤드리스로 A4 PDF 출력

사용: python build.py
"""
import re, subprocess, sys
from pathlib import Path

HERE = Path(__file__).parent
CHROME = r"C:\Program Files\Google\Chrome\Application\chrome.exe"
FONT_DIR = Path.home() / "AppData/Local/Microsoft/Windows/Fonts"
WEIGHTS = {500: "SUITE-Medium.ttf", 700: "SUITE-Bold.ttf", 800: "SUITE-ExtraBold.ttf", 900: "SUITE-Heavy.ttf"}

html = (HERE / "index.html").read_text(encoding="utf-8")
text = re.sub(r"<style>.*?</style>", "", html, flags=re.S)
text = re.sub(r"<[^>]+>", "", text)
chars = "".join(sorted(set(text) | set("0123456789%★▼·")))
(HERE / "fonts" / "_chars.txt").write_text(chars, encoding="utf-8")

for w, src in WEIGHTS.items():
    out = HERE / "fonts" / f"SUITE-{w}.ttf"
    subprocess.run([sys.executable, "-m", "fontTools.subset", str(FONT_DIR / src),
                    f"--text-file={HERE / 'fonts' / '_chars.txt'}",
                    f"--output-file={out}", "--layout-features=*"], check=True)
    print(out.name, out.stat().st_size // 1024, "KB")

pdf = HERE / "설명서.pdf"
subprocess.run([CHROME, "--headless=new", "--disable-gpu", "--no-pdf-header-footer",
                "--virtual-time-budget=5000", f"--print-to-pdf={pdf}",
                (HERE / "index.html").as_uri()], check=True, capture_output=True)
print(pdf.name, pdf.stat().st_size // 1024, "KB")
