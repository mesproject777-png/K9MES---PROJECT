# SN Type Field Reference Guide

Based on QMS3 V4 Serial Number Types Manual - Rev 1.22

## Field Type Categories & Examples

### 1. Date/Time Fields

#### RY - Reliance Year
- **Description:** Reliance year encoding
- **Example:** 2014 is A, 2015 is B and so on
- **field_string:** N/A (leave blank)
- **field_size:** N/A
- **Database:** field_type = "RY"

---

#### RM - Reliance Month
- **Description:** Reliance month encoding
- **Example:** January is A, February is B and so on
- **field_string:** N/A (leave blank)
- **field_size:** N/A
- **Database:** field_type = "RM"

---

#### RMA - Reliance RMA Indicator
- **Description:** Reliance RMA indicator (first part non RMA and 2nd for RMA)
- **field_string:** N/A (leave blank)
- **field_size:** N/A
- **Database:** field_type = "RMA"

#### Y - Single Digit Year
- **Description:** Last digit of current year
- **Example:** 2013 → 3, 2024 → 4
- **field_string:** N/A (leave blank)
- **field_size:** N/A
- **Database:** field_type = "Y"

```json
{
  "sort_order": 10,
  "field_type": "Y",
  "field_string": null,
  "field_size": null
}
```

---

#### YY - Two Digit Year
- **Description:** Last two digits of year
- **Example:** 2013 → 13, 2024 → 24
- **field_string:** N/A
- **field_size:** N/A
- **Note:** Most commonly used for year prefix

```json
{
  "sort_order": 10,
  "field_type": "YY",
  "field_string": null,
  "field_size": null
}
```

---

#### YYY - Full Year (4 digits)
- **Description:** Complete year value
- **Example:** 2013, 2024
- **field_string:** N/A
- **field_size:** N/A

```json
{
  "sort_order": 10,
  "field_type": "YYY",
  "field_string": null,
  "field_size": null
}
```

---

#### M (hex) - Month Hexadecimal
- **Description:** Current month in hexadecimal
- **Mapping:** Jan→1, Feb→2, ..., Oct→A, Nov→B, Dec→C
- **field_string:** N/A
- **field_size:** N/A
- **Use Case:** When compact representation needed

```json
{
  "sort_order": 20,
  "field_type": "M(hex)",
  "field_string": null,
  "field_size": null
}
```

---

#### MM (dec) - Month Decimal
- **Description:** Current month in decimal with leading zero
- **Mapping:** Jan→01, Feb→02, ..., Oct→10, Nov→11, Dec→12
- **field_string:** N/A
- **field_size:** N/A
- **Note:** Most commonly used for month

```json
{
  "sort_order": 20,
  "field_type": "MM(dec)",
  "field_string": null,
  "field_size": null
}
```

---

#### R_YY - Reversed Two Digit Year
- **Description:** Last two digits of year in reversed format
- **Example:** 2013 → 31, 2024 → 42
- **field_string:** N/A
- **field_size:** N/A

```json
{
  "sort_order": 10,
  "field_type": "R_YY",
  "field_string": null,
  "field_size": null
}
```

---

#### R_MM (dec) - Reversed Month Decimal
- **Description:** Month in reversed format
- **Mapping:** Jan→10, Feb→20, ..., Oct→01
- **field_string:** N/A
- **field_size:** N/A

```json
{
  "sort_order": 20,
  "field_type": "R_MM(dec)",
  "field_string": null,
  "field_size": null
}
```

---

#### WW - Week of Year
- **Description:** Current week number (01-52)
- **Range:** 01 (January) to 52 (December)
- **field_string:** N/A
- **field_size:** N/A
- **Use Case:** Batch identification by week

```json
{
  "sort_order": 15,
  "field_type": "WW",
  "field_string": null,
  "field_size": null
}
```

---

#### R_WW - Reversed Week of Year
- **Description:** Week of year in reversed format
- **field_string:** N/A
- **field_size:** N/A

```json
{
  "sort_order": 15,
  "field_type": "R_WW",
  "field_string": null,
  "field_size": null
}
```

---

#### DM - Day of Week
- **Description:** Day on which SN was generated
- **Mapping:** Sunday→1, Monday→2, ..., Saturday→7
- **field_string:** N/A
- **field_size:** N/A

```json
{
  "sort_order": 13,
  "field_type": "DM",
  "field_string": null,
  "field_size": null
}
```

---

#### DD - Date of Month
- **Description:** Current date (day of month)
- **Range:** 01-31
- **field_string:** N/A
- **field_size:** N/A

```json
{
  "sort_order": 15,
  "field_type": "DD",
  "field_string": null,
  "field_size": null
}
```

---

#### DDD - Day of Year
- **Description:** Day number within the year
- **Range:** 001-365
- **field_string:** N/A
- **field_size:** N/A
- **Use Case:** Sequential daily identification

```json
{
  "sort_order": 15,
  "field_type": "DDD",
  "field_string": null,
  "field_size": null
}
```

---

### 2. Counter/Sequence Fields

#### Sequence(dec) - Decimal Counter
- **Description:** Unique decimal counter (0-9 digits)
- **field_string:** N/A
- **field_size:** Required (1-8) - number of digits
- **Behavior:** Increments and doesn't reset
- **Example:** With size=5: 00001, 00002, 00003, ..., 99999

```json
{
  "sort_order": 30,
  "field_type": "Sequence(dec)",
  "field_string": null,
  "field_size": 5
}
```

---

#### Sequence(hex) - Hexadecimal Counter
- **Description:** Unique hexadecimal counter (0-9, A-F)
- **field_string:** N/A
- **field_size:** Required (1-8) - number of hex digits
- **Example:** With size=3: 001, 002, ..., FFF

```json
{
  "sort_order": 30,
  "field_type": "Sequence(hex)",
  "field_string": null,
  "field_size": 3
}
```

---

#### Sequence(alpha) - Alphanumeric Counter
- **Description:** Unique alphanumeric counter (0-9, A-Z)
- **field_string:** N/A
- **field_size:** Required (1-8) - number of characters
- **Example:** With size=4: 0001, 0002, ..., ZZZZ

```json
{
  "sort_order": 30,
  "field_type": "Sequence(alpha)",
  "field_string": null,
  "field_size": 4
}
```

---

#### Continuous sequence(dec) - Continuous Decimal Counter
- **Description:** Decimal counter that resets when reaching max length
- **field_string:** N/A
- **field_size:** Required (1-8)
- **Behavior:** Resets to 1 when max reached
- **Example:** With size=3: 001, 002, ..., 999, 001, 002, ...
- **Use Case:** Monthly counters that reset

```json
{
  "sort_order": 40,
  "field_type": "Continuous sequence(dec)",
  "field_string": null,
  "field_size": 3
}
```

---

#### Continuous sequence(hex) - Continuous Hex Counter
- **Description:** Hexadecimal counter with reset behavior
- **field_string:** N/A
- **field_size:** Required (1-8)
- **Behavior:** Resets to 1 when max reached

```json
{
  "sort_order": 40,
  "field_type": "Continuous sequence(hex)",
  "field_string": null,
  "field_size": 3
}
```

---

#### Continuous sequence(alpha) - Continuous Alphanumeric Counter
- **Description:** Alphanumeric counter with reset behavior
- **field_string:** N/A
- **field_size:** Required (1-8)
- **Behavior:** Resets to 1 when max reached

```json
{
  "sort_order": 40,
  "field_type": "Continuous sequence(alpha)",
  "field_string": null,
  "field_size": 4
}
```

---

### 3. Static/String Fields

#### String - Constant String
- **Description:** Any constant string in the SN
- **Allowed Characters:** a-z, A-Z, hyphen (-), underscore (_)
- **field_string:** Required - the actual string to include
- **field_size:** N/A
- **Example:** "TEST", "PROD", "DEVICE"

```json
{
  "sort_order": 15,
  "field_type": "String",
  "field_string": "TEST",
  "field_size": null
}
```

Result SN with YY-TEST-XXXX: "24TEST0001"

---

#### Specific by PN - PN-Specific Field
- **Description:** Field based on Part Number specific data
- **field_string:** Required - field reference (field1-field99)
- **field_size:** N/A
- **Note:** Pre-defined by MS3 team per customer
- **Example:** "field1", "field2", etc.

```json
{
  "sort_order": 20,
  "field_type": "Specific by PN",
  "field_string": "field1",
  "field_size": null
}
```

---

### 4. Operational Fields

#### WO - Work Order Number
- **Description:** Includes WO number in SN (when generated for specific WO)
- **field_string:** N/A
- **field_size:** N/A
- **Use Case:** Track SNs by work order
- **Note:** Only active when SNs generated in WO context

```json
{
  "sort_order": 25,
  "field_type": "WO",
  "field_string": null,
  "field_size": null
}
```

---

#### Lot - Lot Number
- **Description:** Includes Lot number in SN (when WO has assigned lot)
- **field_string:** N/A
- **field_size:** N/A
- **Use Case:** Track SNs by manufacturing lot
- **Prerequisite:** WO must have an assigned lot

```json
{
  "sort_order": 26,
  "field_type": "Lot",
  "field_string": null,
  "field_size": null
}
```

---

### 5. Site & Code Fields

#### SiteCode - Site Code Translation
- **Description:** Translates site_code to number/letter mapping
- **Mapping:** 0-9 → 0-9, then 10-35 → A-Z
- **Examples:** 
  - 9 → '9'
  - 10 → 'A'
  - 31 → 'V'
- **field_string:** N/A
- **field_size:** N/A

```json
{
  "sort_order": 20,
  "field_type": "SiteCode",
  "field_string": null,
  "field_size": null
}
```

---

### 6. Special/Advanced Fields

#### SNFromEPV - Generate SN from EPV
- **Description:** Takes EPV number and converts it to SN
- **field_string:** Required - EPV Type and Subtype selection
- **field_size:** N/A
- **Restrictions:** 
  - Only one per SN type
  - Can only have EPV, Programmable, MACgen fields
  - Must be last field
- **Performance:** Best to generate max 20K SNs per service call

```json
{
  "sort_order": 50,
  "field_type": "SNFromEPV",
  "field_string": "imei_rnbiot",
  "field_size": null
}
```

---

#### EPV - External Provided Value
- **Description:** Attaches EPV number to the SN
- **field_string:** N/A
- **field_size:** N/A
- **Use Case:** Link external values to SNs
- **Requirement:** When used with SNFromEPV, must be last field

```json
{
  "sort_order": 45,
  "field_type": "EPV",
  "field_string": null,
  "field_size": null
}
```

---

#### MACgen - MAC Address
- **Description:** Attach MAC address to SN
- **field_string:** Required - MAC type (e.g., "ethernet", "wifi", "cellular")
- **field_size:** N/A
- **Note:** Can connect multiple MACs when types differ

```json
{
  "sort_order": 40,
  "field_type": "MACgen",
  "field_string": "ethernet",
  "field_size": null
}
```

Multiple MACs Example:
```json
[
  {
    "sort_order": 40,
    "field_type": "MACgen",
    "field_string": "ethernet"
  },
  {
    "sort_order": 41,
    "field_type": "MACgen",
    "field_string": "wifi"
  }
]
```

---

#### Programmable - Programmable Field
- **Description:** Field that can be programmed dynamically
- **field_string:** N/A
- **field_size:** N/A
- **Use Case:** Flexible fields for future enhancements
- **Allowed With:** SNFromEPV type fields only

```json
{
  "sort_order": 35,
  "field_type": "Programmable",
  "field_string": null,
  "field_size": null
}
```

---

## SN Type Examples

### Example 1: Basic YYMMXXXXX

**Use Case:** Year-Month-Counter pattern

**Fields:**
```json
[
  {
    "sort_order": 10,
    "field_type": "YY"
  },
  {
    "sort_order": 20,
    "field_type": "MM(dec)"
  },
  {
    "sort_order": 30,
    "field_type": "Sequence(dec)",
    "field_size": 5
  }
]
```

**Example SNs:** 
- 2409001 (24=2024, 09=September, 01=first SN)
- 2409002
- 2409003

---

### Example 2: Complex with Multiple Fields

**Use Case:** Complex tracking including site, date, and identifiers

**Fields:**
```json
[
  {
    "sort_order": 10,
    "field_type": "SiteCode"
  },
  {
    "sort_order": 20,
    "field_type": "YY"
  },
  {
    "sort_order": 30,
    "field_type": "WW"
  },
  {
    "sort_order": 40,
    "field_type": "String",
    "field_string": "PROD"
  },
  {
    "sort_order": 50,
    "field_type": "WO"
  },
  {
    "sort_order": 60,
    "field_type": "Sequence(alpha)",
    "field_size": 3
  }
]
```

**Pattern:** SITE-YY-WW-PROD-WO-AAA

---

### Example 3: EPV-Based

**Use Case:** Generate from external system (EPV)

**Fields:**
```json
[
  {
    "sort_order": 10,
    "field_type": "Programmable"
  },
  {
    "sort_order": 20,
    "field_type": "SNFromEPV",
    "field_string": "imei_rnbiot"
  },
  {
    "sort_order": 30,
    "field_type": "MACgen",
    "field_string": "ethernet"
  },
  {
    "sort_order": 40,
    "field_type": "EPV"
  }
]
```

**Note:** Must have EPV as last field when using SNFromEPV

---

## Field Type Validation Rules

### Counter Field Rules
✅ Only one counter allowed per SN type (non-continuous)
✅ Multiple continuous counters allowed (not recommended)
✅ Counter must be last field
✅ Continuous counter can be anywhere
✅ Field size must be 1-8 digits/characters

### Field Size Constraints
| Field Type | Min | Max | Requirement |
|-----------|-----|-----|-------------|
| Sequence(dec) | 1 | 8 | Required |
| Sequence(hex) | 1 | 8 | Required |
| Sequence(alpha) | 1 | 8 | Required |
| Continuous seq(dec) | 1 | 8 | Required |
| Continuous seq(hex) | 1 | 8 | Required |
| Continuous seq(alpha) | 1 | 8 | Required |
| Others | N/A | N/A | N/A |

### String Validation
- **Allowed characters:** a-z, A-Z, 0-9, hyphen (-), underscore (_)
- **Max length:** 500 characters
- **Case sensitive:** Yes

---

## Field Ordering Strategy

**Best Practice Sort Orders:**

```
10 - Year/Date prefix
15 - Secondary date (day, week)
20 - Month or tertiary date
25 - Static strings/site codes
30 - Work order or lot information
40 - Primary counter
50 - EPV or special fields
60 - MAC or additional data
```

**Flexible Example:** Use decimals for insertion flexibility
- 10, 15, 20, 25, 30 → Can insert between: 10, 12, 15, 17, 20, etc.

---

## API Request Templates

### Add Year Field
```bash
POST /api/sn-types/1/fields
{
  "sort_order": 10,
  "field_type": "YY"
}
```

### Add Month Field
```bash
POST /api/sn-types/1/fields
{
  "sort_order": 20,
  "field_type": "MM(dec)"
}
```

### Add Counter (Decimal, 5 digits)
```bash
POST /api/sn-types/1/fields
{
  "sort_order": 30,
  "field_type": "Sequence(dec)",
  "field_size": 5
}
```

### Add String Constant
```bash
POST /api/sn-types/1/fields
{
  "sort_order": 25,
  "field_type": "String",
  "field_string": "PROD"
}
```

### Add Work Order
```bash
POST /api/sn-types/1/fields
{
  "sort_order": 40,
  "field_type": "WO"
}
```

---

## FAQ

**Q: Can I have multiple counters?**
A: Only one primary counter. Multiple continuous counters allowed but not recommended.

**Q: What happens to counter when it reaches max?**
A: Sequence counter continues beyond. Continuous counter resets to 1.

**Q: Is field_size always required?**
A: Only for Sequence types (dec/hex/alpha). All others: N/A.

**Q: Can I use 00000 as counter start?**
A: Yes. Sequence counters start from 1 by default, but can be configured.

**Q: What's the performance impact of large counter sizes?**
A: Negligible. Use 5-6 digits for typical needs (provides 99999-999999 combinations).

---

**Document Date:** July 10, 2024
**Based on:** QMS3 V4 - Search SN User Manual Rev 1.22
