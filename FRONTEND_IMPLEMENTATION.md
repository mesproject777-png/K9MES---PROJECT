# SN Type Frontend Implementation Guide

## Overview
The SN Type frontend has been successfully integrated into the Angular application under the **Engineering > SN Type** tab. It provides a complete UI for managing serial number types with full CRUD operations.

## Files Created/Modified

### New Files Created

#### 1. Service
- **File:** `src/app/services/sn-type.service.ts`
- **Purpose:** Handles all API communication with backend
- **Key Methods:**
  - `getSNTypes()` - Get all SN types
  - `getSNTypeById(id)` - Get specific SN type with fields
  - `createSNType(data)` - Create new SN type
  - `updateSNType(id, data)` - Update SN type
  - `deleteSNType(id)` - Delete SN type
  - `addField(snTypeId, field)` - Add field to SN type
  - `updateField(fieldId, field)` - Update field
  - `deleteField(fieldId)` - Delete field
  - `getFieldTypes()` - Get all allowed field types

#### 2. Components
- **File:** `src/app/dashboard/engineering/sntype/sntype.component.ts`
  - Main component handling SN Type management
  - Displays list of SN types on left
  - Shows details and fields on right
  - Includes create, edit, delete, add field functionality

- **File:** `src/app/dashboard/engineering/sntype/sntype.component.html`
  - Responsive layout with 2-column design
  - Form for creating new SN types
  - Form for editing SN type details
  - Form for adding fields to SN type
  - Table displaying all fields with actions

- **File:** `src/app/dashboard/engineering/sntype/sntype.component.scss`
  - Professional styling
  - Responsive design (mobile-friendly)
  - Color scheme matching dashboard
  - Hex-style component styling

#### 3. Pipes
- **File:** `src/app/pipes/sort.pipe.ts`
- **Purpose:** Sort arrays by specified field
- **Usage:** `{{ fields | sort:'sort_order' }}`

### Modified Files

#### 1. Routing
- **File:** `src/app/app-routing-module.ts`
  - Added import for `SnTypeComponent`
  - Added route: `/dashboard/engineering/sntype`
  - Configured with permission guard

#### 2. Module Declaration
- **File:** `src/app/app-module.ts`
  - Added `SnTypeComponent` to declarations
  - Added `SortPipe` to declarations
  - Added import for both

#### 3. Engineering Menu
- **File:** `src/app/dashboard/engineering/engineeringmenu/engineeringmenu.component.html`
  - Added SN Type hexagon tile
  - Routes to `/dashboard/engineering/sntype`

## Architecture

```
Engineering Tab
├── Menu (Hexagon View)
│   ├── Product Line
│   ├── PN Type
│   └── SN Type ← NEW
│       └── SN Type Component
│           ├── Service (API calls)
│           ├── List View (left panel)
│           ├── Detail View (right panel)
│           ├── Create Form
│           ├── Edit Form
│           └── Field Management
└── Detail Views
```

## Features Implemented

### 1. **List SN Types**
- Displays all SN types in scrollable list
- Shows SN type name and field count
- Shows creation date
- Highlights selected SN type
- Hover effects for better UX

### 2. **Create New SN Type**
- Click "Create New SN Type" button
- Enter SN type name (required)
- Enter optional remark
- Automatically adds default Year field
- Confirmation messages

### 3. **View SN Type Details**
- Click on SN type in list
- Shows SN type name, remark
- Shows creation and update dates
- Displays all associated fields

### 4. **Edit SN Type**
- Click "Edit" button
- Modify name and remark
- Save changes
- Cancel returns to view mode

### 5. **Delete SN Type**
- Click "Delete" button
- Confirmation dialog
- Deletes SN type and all fields (cascade)
- Updates UI automatically

### 6. **Add Fields**
- Click "+ Add Field" button
- Select field type from dropdown
- Enter sort order (decimal number)
- For sequence types: specify size (1-8)
- For string types: enter string value
- Validation and error handling

### 7. **Field Management**
- View all fields in sortable table
- Sort order displayed
- Field type with description
- String value or size info
- Delete individual fields
- Validation feedback

## UI Layout

```
┌─────────────────────────────────────────────────────────┐
│  Serial Number Types          [+ Create New SN Type]    │
├───────────────────┬───────────────────────────────────────┤
│                   │                                       │
│  SN Types List    │  SN Type Details                     │
│                   │  ┌─────────────────────────────────┐ │
│  ┌─────────────┐  │  │ sn01                   [Edit]   │ │
│  │ sn01 (2)    │  │  │ [Delete]                        │ │
│  │ sn02 (4)    │  │  │                                 │ │
│  │ sn03 (1)    │  │  │ SN Type Fields                  │ │
│  │             │  │  │ [+ Add Field]                   │ │
│  │             │  │  │                                 │ │
│  │             │  │  │ Sort│ Type        │ Value│ Del  │
│  │             │  │  │ 10  │ Y           │      │ 🗑    │
│  │             │  │  │ 20  │ MM(dec)     │      │ 🗑    │
│  │             │  │  │ 30  │ Seq(dec)    │ (5)  │ 🗑    │
│  │             │  │  │                                 │ │
│  └─────────────┘  │  └─────────────────────────────────┘ │
│                   │                                       │
└───────────────────┴───────────────────────────────────────┘
```

## Connection with Backend

### API Endpoints Used

| Method | Endpoint | Purpose |
|--------|----------|---------|
| GET | `/api/sn-types` | Get all SN types |
| GET | `/api/sn-types/:id` | Get SN type with fields |
| POST | `/api/sn-types` | Create new SN type |
| PUT | `/api/sn-types/:id` | Update SN type |
| DELETE | `/api/sn-types/:id` | Delete SN type |
| POST | `/api/sn-types/:id/fields` | Add field |
| DELETE | `/api/sn-types/fields/:id` | Delete field |
| GET | `/api/sn-types/reference/field-types` | Get field types |

### Service Flow

```
Component (SnTypeComponent)
    ↓
Service (SnTypeService)
    ├── HTTP Calls
    └── State Management (BehaviorSubjects)
         ↓
    Backend API (Node.js/Express)
         ↓
    Database (PostgreSQL)
```

### Error Handling

- HTTP errors caught and displayed to user
- Validation on frontend before submission
- Backend validation error messages shown
- Loading spinner during API calls
- Success/error messages auto-hide after 3 seconds

## Testing the Frontend

### Prerequisites
1. Backend server running: `npm start` (in mesapi folder)
2. Angular app running: `ng serve` (in K9 folder)
3. Navigate to `http://localhost:4200/dashboard/engineering`

### Test Steps

#### 1. Access SN Type Module
1. Login to dashboard
2. Go to Engineering from sidebar
3. Click on "SN Type" hexagon
4. Should see empty list with "Create New SN Type" button

#### 2. Create SN Type
1. Click "Create New SN Type"
2. Enter name: `sn01`
3. Enter remark: `Test SN Type`
4. Click Create
5. Should see success message and sn01 in list

#### 3. View SN Type
1. Click on sn01 in list
2. Should see details panel with name, remark, dates
3. Should see default Year field

#### 4. Add Fields
1. Click "+ Add Field"
2. Choose Field Type: MM(dec)
3. Sort Order: 20
4. Click "Add Field"
5. Should see new field in table

#### 5. Add Counter Field
1. Click "+ Add Field"
2. Choose Field Type: Sequence(dec)
3. Sort Order: 30
4. Size: 5
5. Click "Add Field"
6. Result: YYMMXXXXX pattern for SN

#### 7. Edit SN Type
1. Click "Edit" button
2. Change name to: `sn01_updated`
3. Click Save
4. Should see updated name

#### 8. Delete Field
1. Click delete icon (🗑️) next to any field
2. Confirm deletion
3. Field should disappear from table

#### 9. Delete SN Type
1. Click "Delete" button
2. Confirm deletion
3. SN type should be removed from list

## Field Type Testing

### Test Date/Time Fields
```
Create SN Type with:
- YY (2-digit year)
- MM(dec) (decimal month)
- DD (date of month)
```

### Test Counter Fields
```
Create SN Type with:
- Sequence(dec) - Size 5 → XXXXX
- Sequence(hex) - Size 3 → XXX (0-9, A-F)
- Sequence(alpha) - Size 4 → XXXX (0-9, A-Z)
```

### Test String Fields
```
Create SN Type with:
- String field_type, value: "TEST"
Result: TEST appears in SN
```

### Test Complex Pattern
```
Create YYMMTESTXXXXX pattern:
1. Y field, sort 10
2. MM(dec) field, sort 20
3. String field "TEST", sort 25
4. Sequence(dec) size 5, sort 30
```

## Styling & Responsive Design

### Desktop View (≥1024px)
- 2-column layout
- List on left, details on right
- Sticky list panel
- Full width forms

### Tablet View (768px - 1024px)
- Stacked layout
- List then details
- Scrollable lists

### Mobile View (<768px)
- Single column
- Collapsible sections
- Touch-friendly buttons
- Optimized spacing

## Color Scheme

- **Primary Blue:** #3498db (buttons, links)
- **Success Green:** #27ae60 (create, save)
- **Error Red:** #e74c3c (delete)
- **Warning Orange:** #f39c12 (edit)
- **Light Gray:** #f5f5f5 (backgrounds)
- **Dark Gray:** #34495e (text, headers)

## Accessibility Features

- ARIA labels on forms
- Keyboard navigation
- Clear error messages
- Loading states
- Confirmation dialogs
- Proper button sizing

## Performance Optimization

- Lazy loading of SN type details
- Efficient sorting with custom pipe
- Limit list height with scrolling
- Debounced API calls
- BehaviorSubjects for state management

## Common Issues & Fixes

| Issue | Cause | Solution |
|-------|-------|----------|
| CORS Error | Backend not running | Start backend: `npm start` |
| No field types shown | Field types API failing | Check backend `/reference/field-types` |
| Can't add 2nd counter | Validation working correctly | Use continuous counters instead |
| Sort order not working | Pipe not registered | Check SortPipe in app.module.ts |
| Styling broken | SCSS not compiled | Run `ng serve` with --poll flag |

## Next Steps / Enhancements

1. **Bulk Operations**
   - Import SN types from CSV
   - Export SN types to Excel
   - Duplicate SN type

2. **Advanced Features**
   - Field type suggestions
   - SN pattern preview
   - Sample SN generation
   - History/audit log

3. **Optimizations**
   - Pagination for large lists
   - Search/filter functionality
   - Field reordering with drag-drop

4. **Integration**
   - SN generation service
   - Part number linking
   - EPV system integration

## Code Structure

```
src/app/
├── services/
│   └── sn-type.service.ts          ← API Communication
├── dashboard/engineering/
│   ├── sntype/
│   │   ├── sntype.component.ts      ← Main Component
│   │   ├── sntype.component.html    ← Template
│   │   └── sntype.component.scss    ← Styling
│   └── engineeringmenu/
│       └── engineeringmenu.component.html  ← Updated with SN Type hex
├── pipes/
│   └── sort.pipe.ts                 ← Sort Utility
└── app-routing-module.ts            ← Updated routing
```

## Development Notes

### Service Patterns Used
- `async/await` for cleaner API calls
- `BehaviorSubjects` for state management
- `RxJS operators` for data transformation
- `Tap operator` for side effects

### Component Patterns Used
- Component lifecycle hooks
- Two-way data binding with `[(ngModel)]`
- Event binding with `(click)`
- Conditional rendering with `*ngIf`
- List rendering with `*ngFor`

### UI Patterns Used
- Material Design principles
- Hex tile layout (consistent with Product Line, PN Type)
- Card-based layout for list items
- Modal-like forms
- Toast notifications

## Browser Support
- Chrome 90+
- Firefox 88+
- Safari 14+
- Edge 90+

## Dependencies
- Angular 14+ (already in project)
- HttpClientModule (already imported)
- FormsModule (already imported)
- RxJS (with project)

---

**Implementation Date:** April 14, 2026
**Status:** ✅ Complete and Tested
**Backend Dependency:** ✅ Connected to Node.js/Express API
**Database:** ✅ Using PostgreSQL tables (sn_types, sn_type_fields)

