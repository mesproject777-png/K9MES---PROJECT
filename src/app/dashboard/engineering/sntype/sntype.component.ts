import { Component, OnInit } from '@angular/core';
import {
  SnTypeService,
  SNType,
  SNTypeField,
  EPVType,
  EPVSubType
} from '../../../services/sn-type.service';

@Component({
  selector: 'app-sn-type',
  standalone: false,
  templateUrl: './sntype.component.html',
  styleUrl: './sntype.component.scss'
})
export class SnTypeComponent implements OnInit {
  snTypes: SNType[] = [];
  selectedSnType: SNType | null = null;
  isLoading = false;
  errorMessage = '';
  successMessage = '';
  
  // Form states
  isEditing = false;
  isCreating = false;
  showFieldForm = false;
  showEditFieldForm = false;
  
  // Edit form data
  editingSnType: SNType = { sn_type_name: '', remark: '' };
  
  // New SN Type form
  newSnType: SNType = { sn_type_name: '', remark: '' };
  
  // New field form
  newField: SNTypeField = {
    sort_order: 10,
    field_type: '',
    field_string: null,
    field_size: null
  };

  editingField: SNTypeField = {
    sort_order: 10,
    field_type: '',
    field_string: null,
    field_size: null
  };
  
  fieldTypes: any = {};
  fieldTypesArray: any[] = [];
  epvTypes: EPVType[] = [];
  epvSubTypes: EPVSubType[] = [];
  selectedEpvTypeId: number | null = null;
  selectedEpvSubTypeId: number | null = null;

  constructor(private snTypeService: SnTypeService) {}

  ngOnInit(): void {
    this.loadSNTypes();
    this.loadFieldTypes();
    this.loadEpvTypes();
  }

  loadSNTypes(): void {
    this.isLoading = true;
    this.snTypeService.getSNTypes().subscribe({
      next: (response: any) => {
        this.snTypes = response.data || [];
        this.isLoading = false;
      },
      error: (error: any) => {
        this.errorMessage = 'Failed to load SN Types';
        console.error('Error loading SN Types:', error);
        this.isLoading = false;
      }
    });
  }

  loadFieldTypes(): void {
    this.snTypeService.getFieldTypes().subscribe({
      next: (types: any) => {
        this.fieldTypes = types;
        this.fieldTypesArray = Object.keys(types).map((key: string) => ({
          key,
          label: types[key]
        }));
      },
      error: (error: any) => {
        console.error('Error loading field types:', error);
      }
    });
  }

  loadEpvTypes(): void {
    this.snTypeService.getEPVTypes().subscribe({
      next: (response) => {
        this.epvTypes = response.data || [];
      },
      error: (error: any) => {
        console.error('Error loading EPV types:', error);
      }
    });
  }

  loadEpvSubTypes(typeId: number, selectFirst = true): void {
    this.snTypeService.getEPVSubTypes(typeId).subscribe({
      next: (response) => {
        this.epvSubTypes = response.data || [];
        const firstSubTypeId = this.epvSubTypes.length > 0 ? this.epvSubTypes[0].id : null;
        if (selectFirst) {
          this.selectedEpvSubTypeId = firstSubTypeId;
        }

        if (this.showFieldForm) {
          this.newField.epv_sub_type_id = firstSubTypeId;
        }

        if (this.showEditFieldForm) {
          this.editingField.epv_sub_type_id = firstSubTypeId;
        }
      },
      error: (error: any) => {
        console.error('Error loading EPV sub-types:', error);
        this.epvSubTypes = [];
        this.selectedEpvSubTypeId = null;
        if (this.showFieldForm) {
          this.newField.epv_sub_type_id = null;
        }
        if (this.showEditFieldForm) {
          this.editingField.epv_sub_type_id = null;
        }
      }
    });
  }

  selectSnType(snType: SNType): void {
    this.selectedSnType = snType;
    this.isEditing = false;
    this.showFieldForm = false;
    this.showEditFieldForm = false;
    
    // Reload to get full details with fields
    if (snType.id) {
      this.snTypeService.getSNTypeById(snType.id).subscribe({
        next: (fullSnType: any) => {
          this.selectedSnType = fullSnType;
        },
        error: (error: any) => {
          this.errorMessage = 'Failed to load SN Type details';
          console.error('Error:', error);
        }
      });
    }
  }

  startCreateNew(): void {
    this.isCreating = true;
    this.newSnType = { sn_type_name: '', remark: '' };
  }

  cancelCreate(): void {
    this.isCreating = false;
    this.newSnType = { sn_type_name: '', remark: '' };
  }

  createSNType(): void {
    if (!this.newSnType.sn_type_name.trim()) {
      this.errorMessage = 'SN Type name is required';
      return;
    }

    this.isLoading = true;
    this.snTypeService.createSNType(this.newSnType).subscribe({
      next: (createdSnType: any) => {
        this.successMessage = 'SN Type created successfully';
        this.newSnType = { sn_type_name: '', remark: '' };
        this.isCreating = false;
        this.loadSNTypes();
        this.selectSnType(createdSnType);
        this.isLoading = false;
        this.clearMessages();
      },
      error: (error: any) => {
        this.errorMessage = 'Failed to create SN Type';
        console.error('Error:', error);
        this.isLoading = false;
        this.clearMessages();
      }
    });
  }

  startEdit(): void {
    if (this.selectedSnType) {
      this.editingSnType = { ...this.selectedSnType };
      this.isEditing = true;
    }
  }

  cancelEdit(): void {
    this.isEditing = false;
    this.editingSnType = { sn_type_name: '', remark: '' };
  }

  updateSNType(): void {
    if (!this.editingSnType.sn_type_name.trim()) {
      this.errorMessage = 'SN Type name is required';
      return;
    }

    if (this.selectedSnType?.id) {
      this.isLoading = true;
      this.snTypeService.updateSNType(this.selectedSnType.id, this.editingSnType).subscribe({
        next: (updatedSnType: any) => {
          this.successMessage = 'SN Type updated successfully';
          this.selectedSnType = updatedSnType;
          this.isEditing = false;
          this.editingSnType = { sn_type_name: '', remark: '' };
          this.loadSNTypes();
          this.isLoading = false;
          this.clearMessages();
        },
        error: (error: any) => {
          this.errorMessage = 'Failed to update SN Type';
          console.error('Error:', error);
          this.isLoading = false;
          this.clearMessages();
        }
      });
    }
  }

  deleteSNType(snType: SNType): void {
    if (confirm(`Are you sure you want to delete SN Type "${snType.sn_type_name}"? All associated fields will also be deleted.`)) {
      if (snType.id) {
        this.isLoading = true;
        this.snTypeService.deleteSNType(snType.id).subscribe({
          next: () => {
            this.successMessage = 'SN Type deleted successfully';
            this.loadSNTypes();
            this.selectedSnType = null;
            this.isLoading = false;
            this.clearMessages();
          },
          error: (error: any) => {
            this.errorMessage = 'Failed to delete SN Type';
            console.error('Error:', error);
            this.isLoading = false;
            this.clearMessages();
          }
        });
      }
    }
  }

  startAddField(): void {
    this.showEditFieldForm = false;
    this.showFieldForm = true;
    this.newField = {
      sort_order: (this.getMaxSortOrder() + 10),
      field_type: '',
      field_string: null,
      field_size: null
    };
    this.selectedEpvTypeId = null;
    this.selectedEpvSubTypeId = null;
    this.epvSubTypes = [];
  }

  cancelAddField(): void {
    this.showFieldForm = false;
    this.newField = {
      sort_order: 10,
      field_type: '',
      field_string: null,
      field_size: null
    };
    this.selectedEpvTypeId = null;
    this.selectedEpvSubTypeId = null;
    this.epvSubTypes = [];
  }

  addField(): void {
    const validationError = this.validateField(this.newField);
    if (validationError) {
      this.errorMessage = validationError;
      return;
    }

    if (this.selectedSnType?.id) {
      this.isLoading = true;
      const payload = this.normalizeFieldPayload(this.newField);
      this.snTypeService.addField(this.selectedSnType.id, payload).subscribe({
        next: () => {
          this.successMessage = 'Field added successfully';
          this.showFieldForm = false;
          this.newField = {
            sort_order: 10,
            field_type: '',
            field_string: null,
            field_size: null
          };
          this.selectSnType(this.selectedSnType!);
          this.loadSNTypes();
          this.isLoading = false;
          this.clearMessages();
        },
        error: (error: any) => {
          this.errorMessage = error.error?.message || 'Failed to add field';
          console.error('Error:', error);
          this.isLoading = false;
          this.clearMessages();
        }
      });
    }
  }

  deleteField(field: SNTypeField): void {
    if (confirm(`Are you sure you want to delete this field?`)) {
      if (field.id) {
        this.isLoading = true;
        this.snTypeService.deleteField(field.id).subscribe({
          next: () => {
            this.successMessage = 'Field deleted successfully';
            if (this.selectedSnType?.id) {
              this.selectSnType(this.selectedSnType);
            }
            this.loadSNTypes();
            this.isLoading = false;
            this.clearMessages();
          },
          error: (error: any) => {
            this.errorMessage = 'Failed to delete field';
            console.error('Error:', error);
            this.isLoading = false;
            this.clearMessages();
          }
        });
      }
    }
  }

  startEditField(field: SNTypeField): void {
    this.showFieldForm = false;
    this.showEditFieldForm = true;
    this.editingField = {
      id: field.id,
      sn_type_id: field.sn_type_id,
      sort_order: field.sort_order,
      field_type: field.field_type,
      field_string: field.field_string ?? null,
      field_size: field.field_size ?? null,
      epv_type_id: field.epv_type_id ?? null,
      epv_sub_type_id: field.epv_sub_type_id ?? null
    };

    this.selectedEpvTypeId = field.epv_type_id ?? null;
    this.selectedEpvSubTypeId = field.epv_sub_type_id ?? null;

    if (this.selectedEpvTypeId) {
      this.loadEpvSubTypes(this.selectedEpvTypeId, false);
    } else {
      this.epvSubTypes = [];
    }
  }

  cancelEditField(): void {
    this.showEditFieldForm = false;
    this.editingField = {
      sort_order: 10,
      field_type: '',
      field_string: null,
      field_size: null,
      epv_type_id: null,
      epv_sub_type_id: null
    };
    this.selectedEpvTypeId = null;
    this.selectedEpvSubTypeId = null;
    this.epvSubTypes = [];
  }

  updateField(): void {
    if (!this.editingField.id) {
      this.errorMessage = 'Invalid field selected for update';
      return;
    }

    const validationError = this.validateField(this.editingField);
    if (validationError) {
      this.errorMessage = validationError;
      return;
    }

    this.isLoading = true;
    const payload = this.normalizeFieldPayload(this.editingField);
    this.snTypeService.updateField(this.editingField.id, payload).subscribe({
      next: () => {
        this.successMessage = 'Field updated successfully';
        this.showEditFieldForm = false;
        if (this.selectedSnType) {
          this.selectSnType(this.selectedSnType);
        }
        this.loadSNTypes();
        this.isLoading = false;
        this.clearMessages();
      },
      error: (error: any) => {
        this.errorMessage = error.error?.message || 'Failed to update field';
        console.error('Error:', error);
        this.isLoading = false;
        this.clearMessages();
      }
    });
  }

  onAddFieldTypeChange(): void {
    this.onFieldTypeChange(this.newField);
    if (this.isEpvFieldType(this.newField.field_type)) {
      if (!this.selectedEpvTypeId && this.epvTypes.length > 0) {
        this.selectedEpvTypeId = this.epvTypes[0].id;
      }
      this.newField.epv_type_id = this.selectedEpvTypeId;
      if (this.selectedEpvTypeId) {
        this.loadEpvSubTypes(this.selectedEpvTypeId);
      }
    }
  }

  onEditFieldTypeChange(): void {
    this.onFieldTypeChange(this.editingField);
    if (this.isEpvFieldType(this.editingField.field_type)) {
      if (!this.selectedEpvTypeId && this.epvTypes.length > 0) {
        this.selectedEpvTypeId = this.epvTypes[0].id;
      }
      this.editingField.epv_type_id = this.selectedEpvTypeId;
      if (this.selectedEpvTypeId) {
        this.loadEpvSubTypes(this.selectedEpvTypeId);
      }
    }
  }

  onFieldTypeChange(field: SNTypeField): void {
    if (!this.isStringType(field.field_type)) {
      field.field_string = null;
    }

    if (!this.isSequenceType(field.field_type)) {
      field.field_size = null;
    }

    if (!this.isEpvFieldType(field.field_type)) {
      field.epv_type_id = null;
      field.epv_sub_type_id = null;
      this.selectedEpvTypeId = null;
      this.selectedEpvSubTypeId = null;
      this.epvSubTypes = [];
    }
  }

  onEpvTypeChange(): void {
    if (!this.selectedEpvTypeId) {
      this.epvSubTypes = [];
      this.selectedEpvSubTypeId = null;
      if (this.showFieldForm) {
        this.newField.epv_type_id = null;
        this.newField.epv_sub_type_id = null;
      }
      if (this.showEditFieldForm) {
        this.editingField.epv_type_id = null;
        this.editingField.epv_sub_type_id = null;
      }
      return;
    }

    if (this.showFieldForm) {
      this.newField.epv_type_id = this.selectedEpvTypeId;
      this.newField.epv_sub_type_id = null;
    }

    if (this.showEditFieldForm) {
      this.editingField.epv_type_id = this.selectedEpvTypeId;
      this.editingField.epv_sub_type_id = null;
    }

    this.loadEpvSubTypes(this.selectedEpvTypeId);
  }

  isEpvFieldType(fieldType: string): boolean {
    return fieldType === 'EPV' || fieldType === 'SNFromEPV';
  }

  isSequenceType(fieldType: string): boolean {
    return ['Sequence(dec)', 'Sequence(hex)', 'Sequence(alpha)', 
            'Continuous sequence(dec)', 'Continuous sequence(hex)', 'Continuous sequence(alpha)'].includes(fieldType);
  }

  isStringType(fieldType: string): boolean {
    return fieldType === 'String' ||
      fieldType === 'Specific by PN' ||
      fieldType === 'MACgen';
  }

  getMaxSortOrder(): number {
    if (!this.selectedSnType?.fields || this.selectedSnType.fields.length === 0) {
      return 0;
    }
    return Math.max(...this.selectedSnType.fields.map((f: SNTypeField) => f.sort_order));
  }

  getFieldTypeLabel(fieldType: string): string {
    return this.fieldTypes[fieldType] || fieldType;
  }

  getSnTypeFieldCount(snType: SNType): number {
    if (typeof snType.number_of_fields === 'number') {
      return snType.number_of_fields;
    }

    return snType.fields?.length || 0;
  }

  private validateField(field: SNTypeField): string | null {
    if (!field.field_type) {
      return 'Field type is required';
    }

    const sortOrder = Number(field.sort_order);
    if (!Number.isFinite(sortOrder) || sortOrder <= 0) {
      return 'Sort order must be a positive number';
    }

    if (this.isStringType(field.field_type)) {
      const value = (field.field_string || '').trim();
      if (!value) {
        return 'String value is required for selected field type';
      }
    }

    if (this.isSequenceType(field.field_type)) {
      const size = Number(field.field_size);
      if (!Number.isInteger(size) || size < 1 || size > 8) {
        return 'Field size for sequence types must be an integer between 1 and 8';
      }
    }

    if (this.isEpvFieldType(field.field_type)) {
      if (!field.epv_type_id || !field.epv_sub_type_id) {
        return 'EPV type and sub-type are required for selected field type';
      }
    }

    return null;
  }

  private normalizeFieldPayload(field: SNTypeField): SNTypeField {
    return {
      sort_order: Number(field.sort_order),
      field_type: field.field_type,
      field_string: this.isStringType(field.field_type) ? (field.field_string || '').trim() : null,
      field_size: this.isSequenceType(field.field_type) ? Number(field.field_size) : null,
      epv_type_id: this.isEpvFieldType(field.field_type) ? Number(field.epv_type_id) : null,
      epv_sub_type_id: this.isEpvFieldType(field.field_type) ? Number(field.epv_sub_type_id) : null
    };
  }

  private clearMessages(): void {
    setTimeout(() => {
      this.successMessage = '';
      this.errorMessage = '';
    }, 3000);
  }
}
