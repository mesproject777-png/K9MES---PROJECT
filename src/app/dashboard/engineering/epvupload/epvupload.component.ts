import { Component, OnInit } from '@angular/core';
import {
  SnTypeService,
  SNType,
  EPVUpload,
  EPVUploadPayload,
  EPVType,
  EPVSubType,
  EPVRegexMasterRow
} from '../../../services/sn-type.service';

@Component({
  selector: 'app-epvupload',
  standalone: false,
  templateUrl: './epvupload.component.html',
  styleUrl: './epvupload.component.scss'
})
export class EpvuploadComponent implements OnInit {
  snTypes: SNType[] = [];
  selectedSnTypeId: number | null = null;
  selectedFile: File | null = null;

  isLoading = false;
  isUploading = false;
  errorMessage = '';
  successMessage = '';

  epvValuesPreview: string[] = [];
  epvValuesTotal = 0;
  uploadHistory: EPVUpload[] = [];

  epvTypes: EPVType[] = [];
  existingSubTypes: EPVSubType[] = [];
  selectedTypeForSubTypeId: number | null = null;
  uploadSubTypes: EPVSubType[] = [];
  uploadSelectedTypeId: number | null = null;
  uploadSelectedSubTypeId: number | null = null;
  regexMasterRows: EPVRegexMasterRow[] = [];

  newTypeName = '';
  newTypeRegex = '';
  newSubTypeName = '';
  newSubTypeRegex = '';

  constructor(private snTypeService: SnTypeService) {}

  ngOnInit(): void {
    this.loadSnTypes();
    this.loadEpvTypes();
    this.loadRegexMaster();
  }

  loadSnTypes(): void {
    this.isLoading = true;
    this.snTypeService.getSNTypes().subscribe({
      next: (response) => {
        this.snTypes = response.data || [];
        if (this.snTypes.length > 0 && !this.selectedSnTypeId) {
          this.selectedSnTypeId = this.snTypes[0].id || null;
        }

        if (this.selectedSnTypeId) {
          this.loadUploadHistory(this.selectedSnTypeId);
        }

        this.isLoading = false;
      },
      error: (error) => {
        console.error('Failed to load SN Types', error);
        this.errorMessage = 'Failed to load SN Types';
        this.isLoading = false;
        this.clearMessages();
      }
    });
  }

  onFileSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    this.selectedFile = input.files && input.files.length > 0 ? input.files[0] : null;
  }

  upload(): void {
    if (!this.selectedSnTypeId) {
      this.errorMessage = 'SN Type context not available for upload';
      this.clearMessages();
      return;
    }

    if (!this.selectedFile) {
      this.errorMessage = 'Please select an EPV file';
      this.clearMessages();
      return;
    }

    if (!this.uploadSelectedTypeId) {
      this.errorMessage = 'Please select EPV type';
      this.clearMessages();
      return;
    }

    if (!this.uploadSelectedSubTypeId) {
      this.errorMessage = 'Please select EPV sub-type';
      this.clearMessages();
      return;
    }

    const selectedTypeId = this.uploadSelectedTypeId;
    const selectedSubTypeId = this.uploadSelectedSubTypeId;

    if (selectedTypeId === null || selectedSubTypeId === null) {
      this.errorMessage = 'Please select EPV type and sub-type';
      this.clearMessages();
      return;
    }

    const lowerName = this.selectedFile.name.toLowerCase();
    const allowed = ['.pdf', '.txt', '.csv', '.json'];
    if (!allowed.some((extension) => lowerName.endsWith(extension))) {
      this.errorMessage = 'Unsupported file format. Allowed: .pdf, .txt, .csv, .json';
      this.clearMessages();
      return;
    }

    this.isUploading = true;
    this.readFileAsBase64(this.selectedFile)
      .then((base64Data) => {
        const payload: EPVUploadPayload = {
          file_name: this.selectedFile!.name,
          mime_type: this.selectedFile!.type || 'application/octet-stream',
          file_content_base64: base64Data,
          epv_type_id: selectedTypeId,
          epv_sub_type_id: selectedSubTypeId
        };

        this.snTypeService.uploadEPVFile(this.selectedSnTypeId!, payload).subscribe({
          next: (response) => {
            this.successMessage = response.message || 'EPV file uploaded successfully';
            this.epvValuesPreview = response.values_preview || [];
            this.epvValuesTotal = response.values_total || 0;
            this.selectedFile = null;
            this.loadUploadHistory(this.selectedSnTypeId!);
            this.isUploading = false;
            this.clearMessages();
          },
          error: (error) => {
            console.error('EPV upload failed', error);
            this.errorMessage = error.error?.message || 'Failed to upload EPV file';
            this.isUploading = false;
            this.clearMessages();
          }
        });
      })
      .catch(() => {
        this.errorMessage = 'Unable to read selected file';
        this.isUploading = false;
        this.clearMessages();
      });
  }

  loadEpvTypes(): void {
    this.snTypeService.getEPVTypes().subscribe({
      next: (response) => {
        this.epvTypes = response.data || [];

        if (!this.selectedTypeForSubTypeId && this.epvTypes.length > 0) {
          this.selectedTypeForSubTypeId = this.epvTypes[0].id;
        }

        if (!this.uploadSelectedTypeId && this.epvTypes.length > 0) {
          this.uploadSelectedTypeId = this.epvTypes[0].id;
        }

        if (this.selectedTypeForSubTypeId) {
          this.loadSubTypesForType(this.selectedTypeForSubTypeId);
        } else {
          this.existingSubTypes = [];
        }

        if (this.uploadSelectedTypeId) {
          this.loadUploadSubTypesForType(this.uploadSelectedTypeId);
        } else {
          this.uploadSubTypes = [];
          this.uploadSelectedSubTypeId = null;
        }
      },
      error: () => {
        this.epvTypes = [];
        this.existingSubTypes = [];
        this.uploadSubTypes = [];
        this.uploadSelectedTypeId = null;
        this.uploadSelectedSubTypeId = null;
      }
    });
  }

  createType(): void {
    const typeName = this.newTypeName.trim();
    const regexRule = this.newTypeRegex.trim();

    if (!typeName) {
      this.errorMessage = 'Type name is required';
      this.clearMessages();
      return;
    }

    if (!regexRule) {
      this.errorMessage = 'Regex is required for new EPV type';
      this.clearMessages();
      return;
    }

    this.snTypeService.createEPVType({ type_name: typeName, regex_rule: regexRule }).subscribe({
      next: (created) => {
        this.successMessage = 'EPV type created successfully';
        this.newTypeName = '';
        this.newTypeRegex = '';
        this.selectedTypeForSubTypeId = created.id;
        this.uploadSelectedTypeId = created.id;
        this.loadEpvTypes();
        this.loadRegexMaster();
        this.clearMessages();
      },
      error: (error) => {
        this.errorMessage = error.error?.message || 'Failed to create EPV type';
        this.clearMessages();
      }
    });
  }

  deleteType(): void {
    if (!this.selectedTypeForSubTypeId) {
      this.errorMessage = 'Select a type to delete';
      this.clearMessages();
      return;
    }

    const selectedType = this.epvTypes.find((type) => type.id === this.selectedTypeForSubTypeId);
    if (!selectedType) {
      this.errorMessage = 'Selected type does not exist';
      this.clearMessages();
      return;
    }

    if (!confirm(`Delete EPV type "${selectedType.type_name}"?`)) {
      return;
    }

    this.snTypeService.deleteEPVType(selectedType.id).subscribe({
      next: (response) => {
        this.successMessage = response.message || 'EPV type deleted successfully';
        this.selectedTypeForSubTypeId = null;
        this.loadEpvTypes();
        this.loadRegexMaster();
        this.clearMessages();
      },
      error: (error) => {
        this.errorMessage = error.error?.message || 'Failed to delete EPV type';
        this.clearMessages();
      }
    });
  }

  onSelectedTypeForSubTypeChange(): void {
    this.newSubTypeName = '';
    this.newSubTypeRegex = '';

    if (this.selectedTypeForSubTypeId) {
      this.loadSubTypesForType(this.selectedTypeForSubTypeId);
    } else {
      this.existingSubTypes = [];
    }
  }

  createSubType(): void {
    if (!this.selectedTypeForSubTypeId) {
      this.errorMessage = 'Select a parent type before adding sub-type';
      this.clearMessages();
      return;
    }

    const subTypeName = this.newSubTypeName.trim();
    const regexRule = this.newSubTypeRegex.trim();

    if (!subTypeName) {
      this.errorMessage = 'Sub-type name is required';
      this.clearMessages();
      return;
    }

    if (!regexRule) {
      this.errorMessage = 'Regex is required for new sub-type';
      this.clearMessages();
      return;
    }

    if (this.existingSubTypes.length > 0) {
      this.errorMessage = 'Only one sub-type is allowed for each EPV type';
      this.clearMessages();
      return;
    }

    const alreadyExists = this.existingSubTypes.some((subType) =>
      subType.sub_type_name.toLowerCase() === subTypeName.toLowerCase()
    );

    if (alreadyExists) {
      this.errorMessage = 'Sub-type already exists for selected type';
      this.clearMessages();
      return;
    }

    this.snTypeService.createEPVSubType(this.selectedTypeForSubTypeId, {
      sub_type_name: subTypeName,
      regex_rule: regexRule
    }).subscribe({
      next: () => {
        this.successMessage = 'Sub-type created successfully';
        this.newSubTypeName = '';
        this.newSubTypeRegex = '';
        this.loadSubTypesForType(this.selectedTypeForSubTypeId!);
        this.loadRegexMaster();
        if (this.uploadSelectedTypeId === this.selectedTypeForSubTypeId) {
          this.loadUploadSubTypesForType(this.uploadSelectedTypeId!);
        }
        this.clearMessages();
      },
      error: (error) => {
        this.errorMessage = error.error?.message || 'Failed to create sub-type';
        this.clearMessages();
      }
    });
  }

  deleteSubType(subType: EPVSubType): void {
    if (!confirm(`Delete sub-type "${subType.sub_type_name}"?`)) {
      return;
    }

    this.snTypeService.deleteEPVSubType(subType.id).subscribe({
      next: (response) => {
        this.successMessage = response.message || 'Sub-type deleted successfully';
        if (this.selectedTypeForSubTypeId) {
          this.loadSubTypesForType(this.selectedTypeForSubTypeId);
        }
        this.loadRegexMaster();
        if (this.uploadSelectedTypeId === this.selectedTypeForSubTypeId) {
          this.loadUploadSubTypesForType(this.uploadSelectedTypeId!);
        }
        this.clearMessages();
      },
      error: (error) => {
        this.errorMessage = error.error?.message || 'Failed to delete sub-type';
        this.clearMessages();
      }
    });
  }

  getExistingSubTypeNamesText(): string {
    if (this.existingSubTypes.length === 0) {
      return 'No sub-type found for selected type.';
    }

    return this.existingSubTypes.map((subType) => subType.sub_type_name).join(', ');
  }

  onUploadTypeChange(): void {
    if (!this.uploadSelectedTypeId) {
      this.uploadSubTypes = [];
      this.uploadSelectedSubTypeId = null;
      return;
    }

    this.loadUploadSubTypesForType(this.uploadSelectedTypeId);
  }

  private loadSubTypesForType(typeId: number): void {
    this.snTypeService.getEPVSubTypes(typeId).subscribe({
      next: (response) => {
        this.existingSubTypes = response.data || [];
      },
      error: () => {
        this.existingSubTypes = [];
      }
    });
  }

  private loadUploadSubTypesForType(typeId: number): void {
    this.snTypeService.getEPVSubTypes(typeId).subscribe({
      next: (response) => {
        this.uploadSubTypes = response.data || [];
        this.uploadSelectedSubTypeId = this.uploadSubTypes.length > 0
          ? this.uploadSubTypes[0].id
          : null;
      },
      error: () => {
        this.uploadSubTypes = [];
        this.uploadSelectedSubTypeId = null;
      }
    });
  }

  private loadRegexMaster(): void {
    this.snTypeService.getEPVRegexMaster().subscribe({
      next: (response) => {
        this.regexMasterRows = response.data || [];
      },
      error: () => {
        this.regexMasterRows = [];
      }
    });
  }

  private loadUploadHistory(snTypeId: number): void {
    this.snTypeService.getEPVUploads(snTypeId).subscribe({
      next: (response) => {
        this.uploadHistory = response.data || [];
      },
      error: () => {
        this.uploadHistory = [];
      }
    });
  }

  private readFileAsBase64(file: File): Promise<string> {
    return new Promise((resolve, reject) => {
      const reader = new FileReader();
      reader.onload = () => {
        const result = reader.result;
        if (typeof result !== 'string') {
          reject(new Error('Invalid file result'));
          return;
        }

        const commaIndex = result.indexOf(',');
        if (commaIndex === -1) {
          reject(new Error('Invalid Data URL format'));
          return;
        }

        resolve(result.slice(commaIndex + 1));
      };

      reader.onerror = () => reject(reader.error || new Error('File read failed'));
      reader.readAsDataURL(file);
    });
  }

  private clearMessages(): void {
    setTimeout(() => {
      this.successMessage = '';
      this.errorMessage = '';
    }, 3000);
  }
}
