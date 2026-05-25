# Quick Start Guide - Testing SN Type API

## Prerequisites
- Node.js installed
- PostgreSQL running
- API server running on `http://localhost:5000`

## 1. Initialize Database

```bash
cd mesapi
npm run init-db
```

Expected output:
```
Connected to PostgreSQL
Created database MESDB (or already exists)
Verified tables: ..., sn_types, sn_type_fields
```

## 2. Start API Server

```bash
npm start
```

Expected output:
```
✅ Server running on port 5000
Server is running on http://localhost:5000
```

## 3. Test API Endpoints

### Using cURL or Postman

#### A. Get All SN Types (should return empty initially)
```bash
curl http://localhost:5000/api/sn-types
```

Response:
```json
{
  "data": [],
  "total": 0
}
```

---

#### B. Create New SN Type: "sn01"
```bash
curl -X POST http://localhost:5000/api/sn-types \
  -H "Content-Type: application/json" \
  -d '{
    "sn_type_name": "sn01",
    "remark": "Year-Month-Counter pattern"
  }'
```

Response:
```json
{
  "id": 1,
  "sn_type_name": "sn01",
  "remark": "Year-Month-Counter pattern",
  "created_at": "2024-07-10T10:30:00Z",
  "updated_at": "2024-07-10T10:30:00Z"
}
```

Save the ID (1) for next steps.

---

#### C. Get SN Type by ID (includes default Year field)
```bash
curl http://localhost:5000/api/sn-types/1
```

Response:
```json
{
  "id": 1,
  "sn_type_name": "sn01",
  "remark": "Year-Month-Counter pattern",
  "created_at": "2024-07-10T10:30:00Z",
  "updated_at": "2024-07-10T10:30:00Z",
  "fields": [
    {
      "id": 1,
      "sn_type_id": 1,
      "sort_order": 10,
      "field_type": "Y",
      "field_string": null,
      "field_size": null
    }
  ]
}
```

---

#### D. Add Month Field
```bash
curl -X POST http://localhost:5000/api/sn-types/1/fields \
  -H "Content-Type: application/json" \
  -d '{
    "sort_order": 20,
    "field_type": "MM(dec)"
  }'
```

Response:
```json
{
  "id": 2,
  "sn_type_id": 1,
  "sort_order": 20,
  "field_type": "MM(dec)",
  "field_string": null,
  "field_size": null,
  "created_at": "2024-07-10T10:35:00Z",
  "updated_at": "2024-07-10T10:35:00Z"
}
```

---

#### E. Add Counter Field
```bash
curl -X POST http://localhost:5000/api/sn-types/1/fields \
  -H "Content-Type: application/json" \
  -d '{
    "sort_order": 30,
    "field_type": "Sequence(dec)",
    "field_size": 5
  }'
```

Response:
```json
{
  "id": 3,
  "sn_type_id": 1,
  "sort_order": 30,
  "field_type": "Sequence(dec)",
  "field_string": null,
  "field_size": 5,
  "created_at": "2024-07-10T10:40:00Z",
  "updated_at": "2024-07-10T10:40:00Z"
}
```

---

#### F. View Complete SN Type
```bash
curl http://localhost:5000/api/sn-types/1
```

Response (now with 3 fields):
```json
{
  "id": 1,
  "sn_type_name": "sn01",
  "remark": "Year-Month-Counter pattern",
  "fields": [
    {
      "id": 1,
      "sort_order": 10,
      "field_type": "Y"
    },
    {
      "id": 2,
      "sort_order": 20,
      "field_type": "MM(dec)"
    },
    {
      "id": 3,
      "sort_order": 30,
      "field_type": "Sequence(dec)",
      "field_size": 5
    }
  ]
}
```

**This creates SN pattern: YYMMXXXXX**
Example SNs: 24060001, 24060002, 24060003, etc.

---

#### G. Update SN Type
```bash
curl -X PUT http://localhost:5000/api/sn-types/1 \
  -H "Content-Type: application/json" \
  -d '{
    "sn_type_name": "sn01_updated",
    "remark": "Updated remark"
  }'
```

---

#### H. Delete Field
```bash
curl -X DELETE http://localhost:5000/api/sn-types/fields/2
```

Response:
```json
{
  "message": "Field deleted successfully"
}
```

---

#### I. Delete SN Type
```bash
curl -X DELETE http://localhost:5000/api/sn-types/1
```

Response:
```json
{
  "message": "SN Type deleted successfully"
}
```

Note: All fields are automatically deleted.

---

#### J. Get Allowed Field Types
```bash
curl http://localhost:5000/api/sn-types/reference/field-types
```

Response:
```json
{
  "Y": "Single digit year",
  "YY": "Two digits year",
  "YYY": "Full year (4 digits)",
  "M(hex)": "Month hexadecimal",
  "MM(dec)": "Month decimal",
  ...
  "Sequence(dec)": "Decimal counter",
  "Sequence(hex)": "Hexadecimal counter",
  "Sequence(alpha)": "Alphanumeric counter",
  ...
}
```

---

## 4. Common Error Scenarios

### Error 1: Adding second counter
```bash
curl -X POST http://localhost:5000/api/sn-types/1/fields \
  -H "Content-Type: application/json" \
  -d '{
    "sort_order": 40,
    "field_type": "Sequence(hex)"
  }'
```

Response (Error):
```json
{
  "message": "Only one counter is allowed per SN type (excluding continuous counters)"
}
```

---

### Error 2: Invalid field size
```bash
curl -X POST http://localhost:5000/api/sn-types/1/fields \
  -H "Content-Type: application/json" \
  -d '{
    "sort_order": 40,
    "field_type": "Sequence(dec)",
    "field_size": 15
  }'
```

Response (Error):
```json
{
  "message": "Field size for sequence types must be between 1 and 8"
}
```

---

### Error 3: Invalid field type
```bash
curl -X POST http://localhost:5000/api/sn-types/1/fields \
  -H "Content-Type: application/json" \
  -d '{
    "sort_order": 40,
    "field_type": "InvalidType"
  }'
```

Response (Error):
```json
{
  "message": "Invalid field type",
  "allowedTypes": ["Y", "YY", "YYY", ...]
}
```

---

## 5. Complete Test Scenario

Create a complex SN type with multiple fields:

```bash
# 1. Create SN Type
curl -X POST http://localhost:5000/api/sn-types \
  -H "Content-Type: application/json" \
  -d '{"sn_type_name":"sn02","remark":"Complex SN Type"}'
# Returns: id: 2

# 2. Add YY (2-digit year) - sort_order: 10 (default Y is replaced conceptually)
# Year field already added, so add month

# 3. Add W (Week)
curl -X POST http://localhost:5000/api/sn-types/2/fields \
  -H "Content-Type: application/json" \
  -d '{"sort_order":15,"field_type":"WW"}'

# 4. Add String constant
curl -X POST http://localhost:5000/api/sn-types/2/fields \
  -H "Content-Type: application/json" \
  -d '{"sort_order":20,"field_type":"String","field_string":"TEST"}'

# 5. Add WorkOrder
curl -X POST http://localhost:5000/api/sn-types/2/fields \
  -H "Content-Type: application/json" \
  -d '{"sort_order":25,"field_type":"WO"}'

# 6. Add Counter
curl -X POST http://localhost:5000/api/sn-types/2/fields \
  -H "Content-Type: application/json" \
  -d '{"sort_order":30,"field_type":"Sequence(alpha)","field_size":3}'

# 7. Get complete SN Type
curl http://localhost:5000/api/sn-types/2
```

Result Pattern: Y-WW-TEST-WO-XXX

---

## 6. Database Verification

```bash
# Connect to PostgreSQL
psql -U postgres -d MESDB

# Check SN Types
SELECT * FROM sn_types;

# Check SN Type Fields
SELECT * FROM sn_type_fields ORDER BY sn_type_id, sort_order;

# Example join query
SELECT 
  st.id, st.sn_type_name, 
  stf.sort_order, stf.field_type, stf.field_size
FROM sn_types st
LEFT JOIN sn_type_fields stf ON st.id = stf.sn_type_id
ORDER BY st.id, stf.sort_order;
```

---

## 7. Postman Collection

Create a Postman collection with these endpoints:

1. **GET** `http://localhost:5000/api/sn-types` - List all
2. **GET** `http://localhost:5000/api/sn-types/1` - Get by ID
3. **POST** `http://localhost:5000/api/sn-types` - Create new
4. **PUT** `http://localhost:5000/api/sn-types/1` - Update
5. **DELETE** `http://localhost:5000/api/sn-types/1` - Delete
6. **POST** `http://localhost:5000/api/sn-types/1/fields` - Add field
7. **PUT** `http://localhost:5000/api/sn-types/fields/1` - Update field
8. **DELETE** `http://localhost:5000/api/sn-types/fields/1` - Delete field
9. **GET** `http://localhost:5000/api/sn-types/reference/field-types` - List field types

---

## 8. Troubleshooting

| Issue | Solution |
|-------|----------|
| Database connection error | Verify PostgreSQL is running: `pg_isready` |
| Tables not found | Run `npm run init-db` |
| Port 5000 in use | Change PORT env var or kill process on port |
| CORS errors | Check CORS headers in app.js |

---

**Next Steps:**
1. Create Angular components for UI
2. Integrate with SN generation service
3. Add authentication/authorization
4. Add audit logging
5. Create comprehensive test suite
