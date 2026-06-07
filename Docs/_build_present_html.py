# -*- coding: utf-8 -*-
"""แปลง เอกสารเตรียมพรีเซนต์.md -> HTML ไฟล์เดียวจบ (อ่านบนมือถือ/กด Print เป็น PDF ได้)"""
import re
import markdown

SRC = r"d:\The_Last\GameCardClientUodate\Docs\เอกสารเตรียมพรีเซนต์.md"
OUT = r"d:\The_Last\GameCardClientUodate\Docs\เอกสารเตรียมพรีเซนต์.html"

with open(SRC, encoding="utf-8") as f:
    text = f.read()

# ทำ checkbox ให้สวยขึ้น: "- [ ] x" -> รายการมีกล่องติ๊ก
text = re.sub(r'^- \[ \] (.*)$', r'- ☐ \1', text, flags=re.M)
text = re.sub(r'^- \[x\] (.*)$', r'- ☑ \1', text, flags=re.M, count=0)

html_body = markdown.markdown(
    text,
    extensions=["tables", "fenced_code", "sane_lists", "toc", "attr_list"],
)

TEMPLATE = """<!DOCTYPE html>
<html lang="th">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>เอกสารเตรียมพรีเซนต์ — เกมการ์ดเสริมสร้างแนวคิดระบบสารสนเทศ</title>
<style>
  :root {{
    --ink:#1f2933; --muted:#52606d; --line:#e4e7eb; --bg:#ffffff;
    --accent:#2563eb; --accent2:#1e40af; --warn-bg:#fff7ed; --warn-bd:#f59e0b;
    --tip-bg:#eff6ff; --code-bg:#f3f4f6; --th:#eef2ff;
  }}
  * {{ box-sizing:border-box; }}
  body {{
    font-family:"Sarabun","Leelawadee UI","Tahoma","Segoe UI",sans-serif;
    color:var(--ink); background:#f6f7f9; margin:0; line-height:1.7;
    -webkit-text-size-adjust:100%;
  }}
  .wrap {{ max-width:860px; margin:0 auto; background:var(--bg);
           padding:28px 22px 80px; box-shadow:0 0 0 1px var(--line); }}
  h1 {{ font-size:1.6rem; line-height:1.35; margin:.2em 0 .1em;
        border-bottom:3px solid var(--accent); padding-bottom:.35em; color:var(--accent2); }}
  h2 {{ font-size:1.28rem; margin:1.8em 0 .5em; padding:.35em .6em;
        background:linear-gradient(90deg,var(--th),transparent); border-left:5px solid var(--accent);
        border-radius:4px; }}
  h3 {{ font-size:1.08rem; margin:1.4em 0 .4em; color:var(--accent2); }}
  h4 {{ font-size:1rem; margin:1.1em 0 .3em; color:var(--muted); }}
  p, li {{ font-size:1rem; }}
  a {{ color:var(--accent); }}
  hr {{ border:0; border-top:1px dashed var(--line); margin:2em 0; }}
  code {{ background:var(--code-bg); padding:.12em .4em; border-radius:5px;
          font-family:"Cascadia Code","Consolas",monospace; font-size:.9em; }}
  pre {{ background:#0f172a; color:#e2e8f0; padding:14px 16px; border-radius:10px;
         overflow:auto; font-size:.82rem; line-height:1.5; }}
  pre code {{ background:transparent; color:inherit; padding:0; }}
  blockquote {{ margin:1em 0; padding:.8em 1em; background:var(--tip-bg);
                border-left:5px solid var(--accent); border-radius:6px; color:#1e3a5f; }}
  blockquote p {{ margin:.3em 0; }}
  table {{ border-collapse:collapse; width:100%; margin:1em 0; font-size:.93rem;
           display:block; overflow-x:auto; }}
  th, td {{ border:1px solid var(--line); padding:8px 10px; text-align:left; vertical-align:top; }}
  th {{ background:var(--th); font-weight:700; white-space:nowrap; }}
  tr:nth-child(even) td {{ background:#fafbfc; }}
  ul, ol {{ padding-left:1.4em; }}
  li {{ margin:.25em 0; }}
  strong {{ color:var(--accent2); }}
  /* callout เน้นหัวข้อ ⚠️ */
  h2:has(+ p), h2 {{ }}
  .topbar {{ position:sticky; top:0; background:var(--accent2); color:#fff;
             font-size:.8rem; padding:6px 12px; text-align:center; letter-spacing:.02em; z-index:5; }}
  @media (max-width:600px){{
    .wrap{{ padding:16px 12px 60px; }}
    h1{{ font-size:1.35rem; }} h2{{ font-size:1.12rem; }}
    body{{ line-height:1.65; }}
  }}
  @media print {{
    body{{ background:#fff; }} .topbar{{ display:none; }}
    .wrap{{ box-shadow:none; max-width:100%; padding:0; }}
    h2{{ break-after:avoid; }} table, pre, blockquote{{ break-inside:avoid; }}
    a{{ color:var(--ink); text-decoration:none; }}
  }}
</style>
</head>
<body>
<div class="topbar">📱 เปิดอ่านบนมือถือได้ • กด Print → Save as PDF เพื่อเก็บไว้อ่านออฟไลน์</div>
<div class="wrap">
{body}
</div>
</body>
</html>
"""

with open(OUT, "w", encoding="utf-8") as f:
    f.write(TEMPLATE.format(body=html_body))

print("WROTE:", OUT)
print("size:", len(TEMPLATE.format(body=html_body)), "chars")
