from pathlib import Path
from PIL import Image, ImageDraw, ImageFont


OUT_DIR = Path(r"D:\honlabo\Codex\outputs\accountingapp-erd")
OUT_PATH = OUT_DIR / "AccountingApp_ERD.png"
FONT_PATH = Path(r"C:\Windows\Fonts\NotoSansJP-VF.ttf")


def font(size, bold=False):
    return ImageFont.truetype(str(FONT_PATH), size=size)


W, H = 2600, 1720
img = Image.new("RGB", (W, H), "#F7FAFC")
draw = ImageDraw.Draw(img)

title_font = font(38, True)
sub_font = font(22)
header_font = font(22, True)
field_font = font(16)
small_font = font(15)
label_font = font(17, True)

COLORS = {
    "title": "#17324D",
    "header": "#1F6F5B",
    "legacy": "#7A4F01",
    "border": "#2D3748",
    "line": "#475569",
    "text": "#172033",
    "muted": "#4A5568",
    "box": "#FFFFFF",
    "pk": "#E8F5E9",
    "fk": "#FFF7ED",
}


tables = {
    "companies": {
        "label": "companies / 会社",
        "x": 70,
        "y": 110,
        "w": 340,
        "fields": [
            ("PK", "company_id"),
            ("", "name"),
            ("", "fiscal_year_start"),
            ("", "created_at"),
        ],
    },
    "users": {
        "label": "users / ユーザー",
        "x": 70,
        "y": 470,
        "w": 340,
        "fields": [
            ("PK", "user_id"),
            ("UK", "login_id"),
            ("", "display_name"),
            ("", "password_hash"),
            ("", "password_salt"),
            ("", "is_active"),
            ("", "created_at / updated_at"),
        ],
    },
    "user_companies": {
        "label": "user_companies / 所属",
        "x": 470,
        "y": 300,
        "w": 390,
        "fields": [
            ("PK/FK", "user_id"),
            ("PK/FK", "company_id"),
            ("", "role"),
            ("", "created_at"),
        ],
    },
    "accounts": {
        "label": "accounts / 勘定科目",
        "x": 930,
        "y": 95,
        "w": 380,
        "fields": [
            ("PK", "account_id"),
            ("FK", "company_id"),
            ("UK", "code"),
            ("", "name"),
            ("", "account_type"),
            ("", "is_control_account"),
            ("FK", "default_tax_code_id"),
            ("", "created_at"),
        ],
    },
    "sub_accounts": {
        "label": "sub_accounts / 補助科目",
        "x": 1390,
        "y": 95,
        "w": 390,
        "fields": [
            ("PK", "sub_account_id"),
            ("FK", "company_id"),
            ("FK", "account_id"),
            ("UK", "code"),
            ("", "name"),
            ("", "external_code"),
            ("", "balance"),
            ("", "is_active"),
        ],
    },
    "sub_account_balances": {
        "label": "sub_account_balances / 月次残高",
        "x": 1870,
        "y": 95,
        "w": 430,
        "fields": [
            ("PK", "balance_id"),
            ("FK", "company_id"),
            ("FK", "sub_account_id"),
            ("UK", "fiscal_year"),
            ("UK", "month"),
            ("", "balance"),
        ],
    },
    "tax_codes": {
        "label": "tax_codes / 税区分",
        "x": 930,
        "y": 610,
        "w": 380,
        "fields": [
            ("PK", "tax_code_id"),
            ("FK", "company_id"),
            ("UK", "code"),
            ("", "name"),
            ("", "tax_kind"),
            ("", "tax_rate"),
            ("", "requires_invoice"),
            ("", "default_purchase_credit_rate"),
        ],
    },
    "business_partners": {
        "label": "business_partners / 取引先",
        "x": 1390,
        "y": 610,
        "w": 390,
        "fields": [
            ("PK", "partner_id"),
            ("FK", "company_id"),
            ("UK", "code"),
            ("", "name"),
            ("", "partner_type"),
            ("", "invoice_status"),
            ("", "registration_number"),
            ("", "is_active"),
        ],
    },
    "journal_vouchers": {
        "label": "journal_vouchers / 仕訳伝票",
        "x": 1870,
        "y": 560,
        "w": 430,
        "fields": [
            ("PK", "voucher_id"),
            ("FK", "company_id"),
            ("", "entry_date"),
            ("UK", "entry_number"),
            ("", "reference"),
            ("FK", "created_by"),
            ("", "created_at / updated_at"),
        ],
    },
    "journal_lines": {
        "label": "journal_lines / 仕訳明細",
        "x": 1870,
        "y": 1000,
        "w": 520,
        "fields": [
            ("PK", "line_id"),
            ("FK", "voucher_id"),
            ("FK", "company_id"),
            ("UK", "line_no"),
            ("", "side"),
            ("FK", "account_id / sub_account_id"),
            ("", "amount"),
            ("FK", "tax_code_id"),
            ("", "tax_amount / creditable_tax_amount"),
            ("FK", "partner_id"),
            ("", "invoice_number / invoice_status"),
            ("", "purchase_credit_rate"),
        ],
    },
}


def table_height(t):
    return 46 + len(t["fields"]) * 30 + 18


def box_rect(name):
    t = tables[name]
    return (t["x"], t["y"], t["x"] + t["w"], t["y"] + table_height(t))


def anchor(name, side):
    x1, y1, x2, y2 = box_rect(name)
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
    header_color = COLORS["legacy"] if t.get("legacy") else COLORS["header"]
    draw.rounded_rectangle((x, y, x + w, y + h), radius=8, fill=COLORS["box"], outline=COLORS["border"], width=2)
    draw.rounded_rectangle((x, y, x + w, y + 46), radius=8, fill=header_color)
    draw.rectangle((x, y + 38, x + w, y + 46), fill=header_color)
    draw.text((x + 14, y + 9), t["label"], font=header_font, fill="#FFFFFF")
    row_y = y + 56
    for key, field in t["fields"]:
        if key:
            fill = COLORS["pk"] if "PK" in key else COLORS["fk"]
            draw.rounded_rectangle((x + 12, row_y - 3, x + 70, row_y + 21), radius=4, fill=fill, outline="#CBD5E1")
            draw.text((x + 18, row_y), key, font=small_font, fill=COLORS["muted"])
        draw.text((x + 84, row_y), field, font=field_font, fill=COLORS["text"])
        row_y += 30


def draw_line(src, src_side, dst, dst_side, label, dashed=False):
    p1 = anchor(src, src_side)
    p2 = anchor(dst, dst_side)
    if src_side in ("right", "left"):
        mid_x = (p1[0] + p2[0]) // 2
        pts = [p1, (mid_x, p1[1]), (mid_x, p2[1]), p2]
    else:
        mid_y = (p1[1] + p2[1]) // 2
        pts = [p1, (p1[0], mid_y), (p2[0], mid_y), p2]
    width = 3
    if dashed:
        for a, b in zip(pts, pts[1:]):
            draw_dashed(a, b, COLORS["line"], width)
    else:
        draw.line(pts, fill=COLORS["line"], width=width, joint="curve")
    lx = (p1[0] + p2[0]) // 2
    ly = (p1[1] + p2[1]) // 2
    draw.rounded_rectangle((lx - 42, ly - 15, lx + 42, ly + 15), radius=5, fill="#FFFFFF", outline="#CBD5E1")
    draw.text((lx - 26, ly - 12), label, font=label_font, fill=COLORS["text"])


def draw_dashed(a, b, fill, width):
    x1, y1 = a
    x2, y2 = b
    dx, dy = x2 - x1, y2 - y1
    length = max(abs(dx), abs(dy))
    if length == 0:
        return
    dash = 16
    gap = 10
    steps = max(1, length // (dash + gap))
    for i in range(steps + 1):
        start = i * (dash + gap)
        end = min(start + dash, length)
        if start >= length:
            break
        sx = x1 + dx * start / length
        sy = y1 + dy * start / length
        ex = x1 + dx * end / length
        ey = y1 + dy * end / length
        draw.line((sx, sy, ex, ey), fill=fill, width=width)


draw.text((70, 34), "AccountingApp ER図", font=title_font, fill=COLORS["title"])
draw.text(
    (70, 78),
    "現在の仕訳データは journal_vouchers / journal_lines。旧方式の journal_entries は廃止済み。",
    font=sub_font,
    fill=COLORS["muted"],
)

for table_name in tables:
    draw_table(table_name)

relations = [
    ("companies", "right", "user_companies", "left", "1:N", False),
    ("users", "right", "user_companies", "left", "1:N", False),
    ("companies", "right", "accounts", "left", "1:N", False),
    ("companies", "right", "tax_codes", "left", "1:N", False),
    ("accounts", "right", "sub_accounts", "left", "1:N", False),
    ("sub_accounts", "right", "sub_account_balances", "left", "1:N", False),
    ("tax_codes", "top", "accounts", "bottom", "1:N", False),
    ("companies", "bottom", "business_partners", "left", "1:N", False),
    ("companies", "right", "journal_vouchers", "left", "1:N", False),
    ("users", "right", "journal_vouchers", "left", "1:N", False),
    ("journal_vouchers", "bottom", "journal_lines", "top", "1:N", False),
    ("accounts", "right", "journal_lines", "left", "1:N", False),
    ("sub_accounts", "bottom", "journal_lines", "left", "1:N", False),
    ("tax_codes", "right", "journal_lines", "left", "1:N", False),
    ("business_partners", "right", "journal_lines", "left", "1:N", False),
]

for rel in relations:
    draw_line(*rel)

legend_x, legend_y = 70, 1545
draw.rounded_rectangle((legend_x, legend_y, legend_x + 790, legend_y + 105), radius=8, fill="#FFFFFF", outline="#CBD5E1")
draw.text((legend_x + 20, legend_y + 18), "凡例", font=header_font, fill=COLORS["title"])
draw.rounded_rectangle((legend_x + 100, legend_y + 20, legend_x + 158, legend_y + 46), radius=4, fill=COLORS["pk"], outline="#CBD5E1")
draw.text((legend_x + 116, legend_y + 23), "PK", font=small_font, fill=COLORS["muted"])
draw.text((legend_x + 175, legend_y + 21), "主キー", font=small_font, fill=COLORS["text"])
draw.rounded_rectangle((legend_x + 280, legend_y + 20, legend_x + 338, legend_y + 46), radius=4, fill=COLORS["fk"], outline="#CBD5E1")
draw.text((legend_x + 296, legend_y + 23), "FK", font=small_font, fill=COLORS["muted"])
draw.text((legend_x + 355, legend_y + 21), "外部キー", font=small_font, fill=COLORS["text"])
draw.line((legend_x + 475, legend_y + 33, legend_x + 555, legend_y + 33), fill=COLORS["line"], width=3)
draw.text((legend_x + 570, legend_y + 21), "通常リレーション", font=small_font, fill=COLORS["text"])
draw_dashed((legend_x + 475, legend_y + 72), (legend_x + 555, legend_y + 72), COLORS["line"], 3)
draw.text((legend_x + 570, legend_y + 60), "移行・参照メモ", font=small_font, fill=COLORS["text"])

OUT_DIR.mkdir(parents=True, exist_ok=True)
img.save(OUT_PATH)
print(OUT_PATH)
