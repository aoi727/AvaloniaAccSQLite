from pathlib import Path
from PIL import Image, ImageDraw, ImageFont


OUT_DIR = Path(r"D:\honlabo\Codex\outputs\accountingapp-erd")
OUT_PATH = OUT_DIR / "AccountingApp_ERD_JournalCentered.png"
FONT_PATH = Path(r"C:\Windows\Fonts\NotoSansJP-VF.ttf")


def font(size):
    return ImageFont.truetype(str(FONT_PATH), size=size)


W, H = 2200, 1500
img = Image.new("RGB", (W, H), "#F7FAFC")
draw = ImageDraw.Draw(img)

title_font = font(34)
sub_font = font(20)
header_font = font(21)
field_font = font(15)
small_font = font(14)
label_font = font(16)

COLORS = {
    "title": "#17324D",
    "header": "#1F6F5B",
    "border": "#334155",
    "line": "#475569",
    "text": "#172033",
    "muted": "#526173",
    "box": "#FFFFFF",
    "pk": "#E8F5E9",
    "fk": "#FFF7ED",
}


tables = {
    "companies": {
        "label": "companies / 会社",
        "x": 90,
        "y": 180,
        "w": 300,
        "fields": [("PK", "company_id"), ("", "name"), ("", "fiscal_year_start")],
    },
    "users": {
        "label": "users / ユーザー",
        "x": 90,
        "y": 620,
        "w": 300,
        "fields": [("PK", "user_id"), ("UK", "login_id"), ("", "display_name"), ("", "is_active")],
    },
    "accounts": {
        "label": "accounts / 勘定科目",
        "x": 1580,
        "y": 100,
        "w": 370,
        "fields": [("PK", "account_id"), ("FK", "company_id"), ("UK", "code"), ("", "name"), ("FK", "default_tax_code_id")],
    },
    "sub_accounts": {
        "label": "sub_accounts / 補助科目",
        "x": 1580,
        "y": 410,
        "w": 370,
        "fields": [("PK", "sub_account_id"), ("FK", "company_id"), ("FK", "account_id"), ("UK", "code"), ("", "name")],
    },
    "tax_codes": {
        "label": "tax_codes / 税区分",
        "x": 1580,
        "y": 720,
        "w": 370,
        "fields": [("PK", "tax_code_id"), ("FK", "company_id"), ("UK", "code"), ("", "name"), ("", "tax_kind / tax_rate")],
    },
    "business_partners": {
        "label": "business_partners / 取引先",
        "x": 1580,
        "y": 1030,
        "w": 430,
        "fields": [("PK", "partner_id"), ("FK", "company_id"), ("UK", "code"), ("", "name"), ("", "invoice_status")],
    },
    "journal_vouchers": {
        "label": "journal_vouchers / 仕訳伝票",
        "x": 700,
        "y": 300,
        "w": 450,
        "fields": [("PK", "voucher_id"), ("FK", "company_id"), ("", "entry_date"), ("UK", "entry_number"), ("", "reference"), ("FK", "created_by")],
    },
    "journal_lines": {
        "label": "journal_lines / 仕訳明細",
        "x": 640,
        "y": 760,
        "w": 560,
        "fields": [
            ("PK", "line_id"),
            ("FK", "voucher_id"),
            ("FK", "company_id"),
            ("UK", "line_no"),
            ("", "side / amount"),
            ("FK", "account_id / sub_account_id"),
            ("FK", "tax_code_id"),
            ("", "tax_amount / creditable_tax_amount"),
            ("FK", "partner_id"),
            ("", "invoice_number / purchase_credit_rate"),
        ],
    },
}


def table_height(table):
    return 46 + len(table["fields"]) * 30 + 16


def rect(name):
    t = tables[name]
    return (t["x"], t["y"], t["x"] + t["w"], t["y"] + table_height(t))


def anchor(name, side):
    x1, y1, x2, y2 = rect(name)
    if side == "left":
        return (x1, (y1 + y2) // 2)
    if side == "right":
        return (x2, (y1 + y2) // 2)
    if side == "top":
        return ((x1 + x2) // 2, y1)
    return ((x1 + x2) // 2, y2)


def draw_table(name):
    t = tables[name]
    x, y, w = t["x"], t["y"], t["w"]
    h = table_height(t)
    draw.rounded_rectangle((x, y, x + w, y + h), radius=8, fill=COLORS["box"], outline=COLORS["border"], width=2)
    draw.rounded_rectangle((x, y, x + w, y + 46), radius=8, fill=COLORS["header"])
    draw.rectangle((x, y + 38, x + w, y + 46), fill=COLORS["header"])
    draw.text((x + 14, y + 10), t["label"], font=header_font, fill="#FFFFFF")
    row_y = y + 58
    for key, field in t["fields"]:
        if key:
            fill = COLORS["pk"] if "PK" in key else COLORS["fk"]
            draw.rounded_rectangle((x + 12, row_y - 4, x + 68, row_y + 20), radius=4, fill=fill, outline="#CBD5E1")
            draw.text((x + 18, row_y - 1), key, font=small_font, fill=COLORS["muted"])
        draw.text((x + 82, row_y), field, font=field_font, fill=COLORS["text"])
        row_y += 30


def draw_path(points, label):
    draw.line(points, fill=COLORS["line"], width=3)
    mid = points[len(points) // 2]
    lx, ly = mid
    draw.rounded_rectangle((lx - 36, ly - 14, lx + 36, ly + 14), radius=5, fill="#FFFFFF", outline="#CBD5E1")
    draw.text((lx - 24, ly - 11), label, font=label_font, fill=COLORS["text"])


def connect(src, src_side, dst, dst_side, label):
    p1 = anchor(src, src_side)
    p2 = anchor(dst, dst_side)
    if src in ("journal_vouchers", "journal_lines") or dst in ("journal_vouchers", "journal_lines"):
        if src_side in ("left", "right"):
            bend_x = (p1[0] + p2[0]) // 2
            points = [p1, (bend_x, p1[1]), (bend_x, p2[1]), p2]
        else:
            bend_y = (p1[1] + p2[1]) // 2
            points = [p1, (p1[0], bend_y), (p2[0], bend_y), p2]
    else:
        points = [p1, p2]
    draw_path(points, label)


draw.text((70, 34), "AccountingApp ER図（仕訳中心レイアウト）", font=title_font, fill=COLORS["title"])
draw.text(
    (70, 78),
    "journal_vouchers と journal_lines を中心に、直接関係するテーブルだけを周囲へ配置した別パターンです。",
    font=sub_font,
    fill=COLORS["muted"],
)

for name in tables:
    draw_table(name)

connect("companies", "right", "journal_vouchers", "left", "1:N")
connect("users", "right", "journal_vouchers", "left", "1:N")
connect("journal_vouchers", "bottom", "journal_lines", "top", "1:N")
connect("companies", "right", "journal_lines", "left", "1:N")
connect("accounts", "left", "journal_lines", "right", "1:N")
connect("sub_accounts", "left", "journal_lines", "right", "1:N")
connect("tax_codes", "left", "journal_lines", "right", "1:N")
connect("business_partners", "left", "journal_lines", "right", "1:N")
connect("tax_codes", "top", "accounts", "bottom", "1:N")
connect("accounts", "bottom", "sub_accounts", "top", "1:N")

legend_x, legend_y = 70, 1330
draw.rounded_rectangle((legend_x, legend_y, legend_x + 720, legend_y + 92), radius=8, fill="#FFFFFF", outline="#CBD5E1")
draw.text((legend_x + 18, legend_y + 16), "凡例", font=header_font, fill=COLORS["title"])
draw.rounded_rectangle((legend_x + 90, legend_y + 18, legend_x + 146, legend_y + 42), radius=4, fill=COLORS["pk"], outline="#CBD5E1")
draw.text((legend_x + 108, legend_y + 20), "PK", font=small_font, fill=COLORS["muted"])
draw.text((legend_x + 164, legend_y + 18), "主キー", font=small_font, fill=COLORS["text"])
draw.rounded_rectangle((legend_x + 258, legend_y + 18, legend_x + 314, legend_y + 42), radius=4, fill=COLORS["fk"], outline="#CBD5E1")
draw.text((legend_x + 276, legend_y + 20), "FK", font=small_font, fill=COLORS["muted"])
draw.text((legend_x + 332, legend_y + 18), "外部キー", font=small_font, fill=COLORS["text"])
draw.line((legend_x + 448, legend_y + 30, legend_x + 528, legend_y + 30), fill=COLORS["line"], width=3)
draw.text((legend_x + 544, legend_y + 18), "テーブル関係", font=small_font, fill=COLORS["text"])

memo_x = 840
memo_y = 1330
draw.rounded_rectangle((memo_x, memo_y, memo_x + 520, memo_y + 92), radius=8, fill="#FFFFFF", outline="#CBD5E1")
draw.text((memo_x + 18, memo_y + 16), "図外テーブル", font=header_font, fill=COLORS["title"])
draw.text((memo_x + 18, memo_y + 52), "user_companies, sub_account_balances", font=small_font, fill=COLORS["text"])

OUT_DIR.mkdir(parents=True, exist_ok=True)
img.save(OUT_PATH)
print(OUT_PATH)
