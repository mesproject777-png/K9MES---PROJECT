# SN Type API Documentation

Based on QMS3 V4 - Serial Number Type Management System

## Overview

The SN Type API allows you to define different SN (Serial Number) structures that can be managed by engineers. Each SN type consists of multiple fields that define its composition and format.

## Base URL
```
http://localhost:5000/api/sn-types
```

## Database Schema

### sn_types table
```sql
CREATE TABLE sn_types (
  id SERIAL PRIMARY KEY,
  sn_type_name VARCHAR(100) NOT NULL,
  remark TEXT,
  created_at TIMESTAMP DEFAULT NOW(),
  updated_at TIMESTAMP DEFAULT NOW()
);
```

### sn_type_fields table
```sql
CREATE TABLE sn_type_fields (
  id SERIAL PRIMARY KEY,
  sn_type_id INTEGER NOT NULL REFERENCES sn_types(id) ON DELETE CASCADE,
  sort_order DECIMAL(10, 2) NOT NULL,
  field_type VARCHAR(50) NOT NULL,
  field_string VARCHAR(500),
  field_size INTEGER,
  created_at TIMESTAMP DEFAULT NOW(),
  updated_at TIMESTAMP DEFAULT NOW(),
  UNIQUE(sn_type_id, sort_order)
);
```

## Main Features

1. ✅ Define new SN structure
2. ✅ Edit existing SN structure
3. ✅ View details of existing SN structure

## API Endpoints

### 1. Get All SN Types
**Endpoint:** `GET /api/sn-types`

**Response:**
```json
{
  "data": [
    {
      "id": 1,
      "sn_type_name": "sn01",
      "remark": "Standard SN Type",
      "created_at": "2024-07-10T10:30:00Z",
      "updated_at": "2024-07-10T10:30:00Z"
    }
  ],
  "total": 1
}
```

---

### 2. Get SN Type by ID (with Fields)
**Endpoint:** `GET /api/sn-types/:id`

**Response:**
```json
{
  "id": 1,
  "sn_type_name": "sn01",
  "remark": "Standard SN Type with YYMMXXXXX pattern",
  "created_at": "2024-07-10T10:30:00Z",
  "updated_at": "2024-07-10T10:30:00Z",
  "fields": [
    {
      "id": 55,
      "sn_type_id": 1,
      "sort_order": 10,
      "field_type": "Y",
      "field_string": null,
      "field_size": null
    },
    {
      "id": 56,
      "sn_type_id": 1,
      "sort_order": 20,
      "field_type": "MM(dec)",
      "field_string": null,
      "field_size": null
    },
    {
      "id": 57,
      "sn_type_id": 1,
      "sort_order": 30,
      "field_type": "Sequence(dec)",
      "field_string": null,
      "field_size": 5
    }
  ]
}
```

---

### 3. Create New SN Type
**Endpoint:** `POST /api/sn-types`

**Request Body:**
```json
{
  "sn_type_name": "sn02",
  "remark": "Optional remark about this SN type"
}
```

**Notes:**
- A default Year (Y) field is automatically added with sort_order 10
- sn_type_name is required

**Response:**
```json
{
  "id": 2,
  "sn_type_name": "sn02",
  "remark": "Optional remark about this SN type",
  "created_at": "2024-07-10T10:35:00Z",
  "updated_at": "2024-07-10T10:35:00Z"
}
```

---

### 4. Update SN Type
**Endpoint:** `PUT /api/sn-types/:id`

**Request Body:**
```json
{
  "sn_type_name": "sn02_updated",
  "remark": "Updated remark"
}
```

**Response:**
```json
{
  "id": 2,
  "sn_type_name": "sn02_updated",
  "remark": "Updated remark",
  "created_at": "2024-07-10T10:35:00Z",
  "updated_at": "2024-07-10T10:40:00Z"
}
```

---

### 5. Delete SN Type
**Endpoint:** `DELETE /api/sn-types/:id`

**Response:**
```json
{
  "message": "SN Type deleted successfully"
}
```

**Note:** All associated fields are automatically deleted (CASCADE)

---

### 6. Add Field to SN Type
**Endpoint:** `POST /api/sn-types/:sn_type_id/fields`

**Request Body:**
```json
{
  "sort_order": 40,
  "field_type": "Sequence(dec)",
  "field_string": null,
  "field_size": 5
}
```

**Field Type Validation Rules:**
1. **Counter Field Rules:**
   - Only one counter per SN type (excluding continuous counters)
   - Counter field should always come last
   - Each SN type must have at least one counter
   - For Sequence types, field_size must be between 1-8

2. **Supported Field Types:**

| Field Type | Description | field_string | field_size |
|-----------|-------------|--------------|-----------|
| RY | Reliance Year (2014=A, 2015=B, ...) | N/A | N/A |
| RM | Reliance Month (Jan=A, Feb=B, ...) | N/A | N/A |
| RMA | Reliance RMA indicator (non-RMA/RMA) | N/A | N/A |
| Y | Single digit year (2013→3) | N/A | N/A |
| YY | Two digits year (2013→13) | N/A | N/A |
| YYY | Full year (2013) | N/A | N/A |
| R_YY | Reversed two digits year | N/A | N/A |
| R_MM(dec) | Reversed month decimal | N/A | N/A |
| R_WW | Reversed week of year | N/A | N/A |
| M(hex) | Month hexadecimal | N/A | N/A |
| MM(dec) | Month decimal | N/A | N/A |
| WW | Week of year | N/A | N/A |
| DM | Day of week | N/A | N/A |
| DD | Date of month | N/A | N/A |
| DDD | Day of year | N/A | N/A |
| String | Constant string | String value | N/A |
| Specific by PN | PN specific field | field1-field99 | N/A |
| Sequence(dec) | Decimal counter | N/A | 1-8 |
| Sequence(hex) | Hexadecimal counter | N/A | 1-8 |
| Sequence(alpha) | Alphanumeric counter | N/A | 1-8 |
| Continuous sequence(dec) | Continuous decimal | N/A | 1-8 |
| Continuous sequence(hex) | Continuous hex | N/A | 1-8 |
| Continuous sequence(alpha) | Continuous alphanumeric | N/A | 1-8 |
| WO | Work Order number | N/A | N/A |
| Lot | Lot number | N/A | N/A |
| SiteCode | Site code translation | N/A | N/A |
| SNFromEPV | Generate SN from EPV | N/A | N/A |
| EPV | External Provided Value | N/A | N/A |
| MACgen | MAC address | MAC type | N/A |
| Programmable | Programmable field | N/A | N/A |

**Response:**
```json
{
  "id": 58,
  "sn_type_id": 1,
  "sort_order": 40,
  "field_type": "Sequence(dec)",
  "field_string": null,
  "field_size": 5,
  "created_at": "2024-07-10T10:45:00Z",
  "updated_at": "2024-07-10T10:45:00Z"
}
```

---

### 7. Update SN Type Field
**Endpoint:** `PUT /api/sn-types/fields/:field_id`

**Request Body:**
```json
{
  "sort_order": 35,
  "field_type": "Sequence(dec)",
  "field_size": 6
}
```

**Response:**
```json
{
  "id": 58,
  "sn_type_id": 1,
  "sort_order": 35,
  "field_type": "Sequence(dec)",
  "field_string": null,
  "field_size": 6,
  "created_at": "2024-07-10T10:45:00Z",
  "updated_at": "2024-07-10T10:50:00Z"
}
```

---

### 8. Delete SN Type Field
**Endpoint:** `DELETE /api/sn-types/fields/:field_id`

**Response:**
```json
{
  "message": "Field deleted successfully"
}
```

---

### 9. Get Allowed Field Types
**Endpoint:** `GET /api/sn-types/reference/field-types`

**Response:**
```json
{
  "RY": "Reliance Year (2014=A, 2015=B, ...)",
  "RM": "Reliance Month (Jan=A, Feb=B, ...)",
  "RMA": "Reliance RMA indicator (non-RMA/RMA)",
  "Y": "Single digit year",
  "YY": "Two digits year",
  "YYY": "Full year (4 digits)",
  "M(hex)": "Month hexadecimal",
  "MM(dec)": "Month decimal",
  "R_YY": "Reversed two digits year",
  "R_MM(dec)": "Reversed month decimal",
  "R_WW": "Reversed week of year",
  "WW": "Week of year",
  "DM": "Day of week",
  "DD": "Date of month",
  "DDD": "Day of year",
  "String": "Constant string",
  "Specific by PN": "PN specific field",
  "Sequence(dec)": "Decimal counter",
  "Sequence(hex)": "Hexadecimal counter",
  "Sequence(alpha)": "Alphanumeric counter",
  "Continuous sequence(dec)": "Continuous decimal counter",
  "Continuous sequence(hex)": "Continuous hexadecimal counter",
  "Continuous sequence(alpha)": "Continuous alphanumeric counter",
  "WO": "Work Order number",
  "Lot": "Lot number",
  "SiteCode": "Site code with translation",
  "SNFromEPV": "Generate SN from EPV",
  "EPV": "External Provided Value",
  "MACgen": "MAC address",
  "Programmable": "Programmable field"
}
```

---

## Example: Creating a Complete SN Type

### Step 1: Create SN Type
```
POST /api/sn-types
Content-Type: application/json

{
  "sn_type_name": "sn03",
  "remark": "YYMMXXXXX pattern - Year Month Counter"
}
```

### Step 2: Add Month Field
```
POST /api/sn-types/1/fields
Content-Type: application/json

{
  "sort_order": 20,
  "field_type": "MM(dec)",
  "field_size": null
}
```

### Step 3: Add Counter Field
```
POST /api/sn-types/1/fields
Content-Type: application/json

{
  "sort_order": 30,
  "field_type": "Sequence(dec)",
  "field_size": 5
}
```

### Result
SN Type "sn03" with pattern YYMMXXXXX will generate SNs like:
- 2409001 (24=year, 09=month, 00001=counter)
- 2409002
- 2409003
- etc.

---

## Error Responses

### 400 Bad Request
```json
{
  "message": "Only one counter is allowed per SN type (excluding continuous counters)"
}
```

### 404 Not Found
```json
{
  "message": "SN Type not found"
}
```

### 500 Internal Server Error
```json
{
  "message": "Database error description"
}
```

---

## Important Notes

1. **Field Order:** Fields are ordered by `sort_order` (ascending). Use decimal numbers for flexibility (e.g., 10, 20, 30 or 10, 15, 20)

2. **Counter Rules:**
   - Only one primary counter per SN type
   - Counter field should come last in the sequence
   - Multiple continuous counters are allowed but not recommended

3. **Generate from EPV:**
   - Only one 'Generate from EPV' allowed per SN Type
   - If used, only EPV, Programmable, and MACgen fields are allowed
   - For best performance, generate max 20K SNs per service call

4. **Cascade Delete:** Deleting an SN Type automatically deletes all its fields

5. **Performance:** For best performance, limit counter size to reasonable values (typically 5-6 digits)

---

## Status Codes

- **200 OK** - Successful GET/PUT
- **201 Created** - Successful POST (resource created)
- **204 No Content** - Successful DELETE
- **400 Bad Request** - Invalid field values or validation failed
- **404 Not Found** - Resource not found
- **500 Internal Server Error** - Database or server error
