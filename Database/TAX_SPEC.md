# AccountingApp 消費税運用仕様

この文書は、AccountingApp における消費税関連機能の運用ルールを整理したものです。  
対象は、会社設定、税区分、取引先インボイス情報、仕訳入力時の税額計算、および `journal_lines` テーブルに保存される税関連項目です。

## 1. 基本方針

本システムでは、消費税情報を仕訳明細単位で管理します。  
各仕訳明細には、税区分、税率、消費税額、控除対象税額、控除対象外税額、インボイス状態、仕入税額控除率を保存します。

仕訳入力時に計算された税額は、明細の属性情報として `journal_lines` に保存されます。  
現行実装では、この税額情報を使って別の税仕訳行を自動生成したり、借方・貸方金額そのものを自動補正したりはしません。

## 2. 会社設定

会社ごとに以下の消費税設定を持ちます。

- `tax_entry_method`
  消費税記帳方式。`gross` または `net`。
- `is_tax_exempt`
  免税事業者かどうか。

運用ルールは次の通りです。

- `gross`
  税込入力を前提とします。
- `net`
  税抜入力を前提とします。
- `is_tax_exempt = true`
  免税事業者として扱い、税区分入力や税額計算は行いません。
  この場合、記帳方式は実質的に `gross` 扱いになります。

## 3. 仕訳入力時の税入力方式

仕訳入力画面では、会社設定に応じて税入力方式の選択肢が変わります。

- `gross` の場合
  `none` または `included`
- `net` の場合
  `none` または `excluded`

各値の意味は次の通りです。

- `none`
  税計算を行いません。
- `included`
  入力金額を税込金額として扱います。
- `excluded`
  入力金額を税抜金額として扱います。

既定値は次の通りです。

- `gross` の場合は `included`
- `net` の場合は `excluded`
- 免税事業者の場合は `none`

なお、`gross` 方式では `仮払消費税` および `仮受消費税` は勘定科目選択候補から除外されます。

## 4. 税区分マスタ

税区分マスタ `tax_codes` では、各税区分について以下を管理します。

- 税区分コード
- 税区分名
- 税種別
  `sales` / `purchase` / `non_taxable` / `exempt` / `out_of_scope`
- 税率
- 仕入税額控除対象かどうか
- 課税対象かどうか
- インボイス判定が必要かどうか
- 既定の仕入税額控除率

標準税区分として、課税売上、課税仕入、非課税、免税、対象外などが初期登録されます。

## 5. 取引先インボイス情報

取引先マスタ `business_partners` では、インボイス制度に関する情報として以下を持ちます。

- `invoice_status`
  `qualified` / `exempt` / `unregistered` / `unknown`
- `registration_number`
  登録番号

この情報は、仕入税額控除率の判定に使用します。

## 6. `journal_lines` の税関連項目

仕訳明細テーブル `journal_lines` では、税関連情報として以下を保持します。

- `tax_code_id`
  適用税区分ID
- `tax_rate`
  保存時点の税率
- `tax_amount`
  当該明細に対応する消費税額
- `creditable_tax_amount`
  仕入税額控除の対象となる税額
- `non_creditable_tax_amount`
  仕入税額控除の対象外となる税額
- `tax_input_type`
  `none` / `included` / `excluded`
- `invoice_number`
  請求書番号
- `invoice_registration_number`
  保存時点の登録番号
- `invoice_status`
  保存時点のインボイス区分
- `purchase_credit_rate`
  保存時点の仕入税額控除率

これらはすべて、仕訳保存時点のスナップショットとして保存されます。  
後から取引先マスタの状態を変更しても、既存仕訳明細の税関連値は自動更新されません。

## 7. 税額計算ルール

税額計算は仕訳明細ごとに行います。計算ルールは以下の通りです。

- 税区分未指定、または非課税区分、または税率0%の場合
  `tax_amount = 0`
  `creditable_tax_amount = 0`
  `non_creditable_tax_amount = 0`

- `tax_input_type = none` の場合
  税計算を行わず、すべて 0

- `tax_input_type = included` の場合
  入力金額を税込金額として扱い、次式で税額を求めます。  
  `tax_amount = amount × tax_rate ÷ (100 + tax_rate)`

- `tax_input_type = excluded` の場合
  入力金額を税抜金額として扱い、次式で税額を求めます。  
  `tax_amount = amount × tax_rate ÷ 100`

- 税額の端数処理
  四捨五入で整数円に丸めます。

## 8. `tax_amount` の意味

`tax_amount` は、当該明細に対応して算出された消費税額です。

運用上の意味は次の通りです。

- 売上系税区分では、その明細に含まれるまたは加算される消費税額を示します。
- 仕入系税区分では、その明細に対応する消費税額を示します。
- 非課税・免税・対象外・税計算なしでは 0 です。

## 9. `creditable_tax_amount` の意味

`creditable_tax_amount` は、仕入税額控除の対象となる税額です。

この項目は、仕入税額控除対象の税区分でのみ意味を持ちます。  
売上系税区分や非課税区分では 0 になります。

控除率が100%であれば `tax_amount` と同額になります。  
控除率が80%や50%であれば、その割合分だけが控除対象となります。

## 10. `non_creditable_tax_amount` の意味

`non_creditable_tax_amount` は、消費税額のうち仕入税額控除できない部分です。

計算式は次の通りです。  
`non_creditable_tax_amount = tax_amount - creditable_tax_amount`

この項目は、仕入税額控除対象の税区分で、かつ控除率が100%未満の場合に意味を持ちます。

なお、仕入関連の税区分であっても、税区分マスタ上 `is_purchase_credit = false` のものについては、現行実装では `creditable_tax_amount` も `non_creditable_tax_amount` も 0 になります。

## 11. 仕入税額控除率の決定

税区分で `is_purchase_credit = true` の場合、控除率を決定します。

- `requires_invoice = false`
  税区分マスタの `default_purchase_credit_rate` を使用します。
- `requires_invoice = true`
  取引先の `invoice_status` と取引日から決定します。

現行ルールは次の通りです。

- `qualified`
  100%
- `unregistered` または `exempt`
  2026年10月1日より前は 80%
- `unregistered` または `exempt`
  2026年10月1日以上 2029年10月1日より前は 50%
- `unregistered` または `exempt`
  2029年10月1日以降は 0%
- `unknown` その他
  0%

## 12. 取引先未選択時の扱い

仕入税額控除対象で、かつインボイス判定が必要な税区分において取引先が未選択の場合、画面上は警告表示されます。

ただし現行実装では保存禁止にはなりません。  
この場合、控除率判定に必要な取引先情報がないため、結果として控除率は 0% 扱いになります。

## 13. 免税事業者の扱い

会社が免税事業者に設定されている場合は、税関連UIを非表示にします。保存時の扱いは次の通りです。

- `tax_code_id` は未設定
- `tax_input_type` は `none`
- `tax_amount = 0`
- `creditable_tax_amount = 0`
- `non_creditable_tax_amount = 0`
- インボイス関連項目は未設定

## 14. CSV入出力

仕訳CSVでは、税関連情報を次の列でそのまま入出力します。

- `tax_code`
- `tax_rate`
- `tax_amount`
- `creditable_tax_amount`
- `non_creditable_tax_amount`
- `tax_input_type`
- `invoice_number`
- `invoice_registration_number`
- `invoice_status`
- `purchase_credit_rate`

このため、CSVエクスポートした仕訳を再インポートした場合、税関連値も原則そのまま復元されます。

一方で、CSV取込時には税額の再計算や整合性再検証は基本的に行われません。  
そのため、外部で編集したCSVに不整合な税額が含まれている場合でも、そのまま登録される可能性があります。

## 15. 運用上の注意

- `tax_amount` は税額の記録用であり、自動的に仮払消費税・仮受消費税の別行を生成するものではありません。
- `creditable_tax_amount` と `non_creditable_tax_amount` は、仕入税額控除判定結果を保存するための項目です。
- 取引先のインボイス区分を変更しても、過去仕訳の税関連値は自動で更新されません。
- 税区分やCSVの運用を誤ると、税額情報と実際の会計処理が一致しない可能性があります。
- 総勘定元帳や試算表の残高計算は、あくまで仕訳金額 `amount` を基準としており、税関連列自体で別計上は行いません。

## 16. 典型例

### 16.1 課税売上 10% を税込で入力する場合

- 会社設定: `gross`
- 税入力方式: `included`
- 金額: 110,000
- 税区分: 課税売上10%

結果:

- `tax_amount = 10,000`
- `creditable_tax_amount = 0`
- `non_creditable_tax_amount = 0`

### 16.2 課税仕入 10% を税抜で入力する場合

- 会社設定: `net`
- 税入力方式: `excluded`
- 金額: 100,000
- 税区分: 課税仕入10%
- 取引先インボイス区分: `qualified`

結果:

- `tax_amount = 10,000`
- `creditable_tax_amount = 10,000`
- `non_creditable_tax_amount = 0`
- `purchase_credit_rate = 100`

### 16.3 課税仕入 10% で免税事業者等からの経過措置対象仕入の場合

- 会社設定: `net`
- 税入力方式: `excluded`
- 金額: 100,000
- 税区分: 課税仕入10%
- 取引先インボイス区分: `exempt`
- 取引日: 2026年9月30日以前

結果:

- `tax_amount = 10,000`
- `creditable_tax_amount = 8,000`
- `non_creditable_tax_amount = 2,000`
- `purchase_credit_rate = 80`
