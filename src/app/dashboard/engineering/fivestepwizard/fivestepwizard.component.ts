import { Component } from '@angular/core';

type WizardStep = {
  label: string;
};

@Component({
  selector: 'app-fivestepwizard',
  standalone: false,
  templateUrl: './fivestepwizard.component.html',
  styleUrl: './fivestepwizard.component.scss'
})
export class FivestepwizardComponent {
  readonly steps: WizardStep[] = [
    { label: 'PartNumber' },
    { label: 'WorkOrder' },
    { label: 'Routing' },
    { label: 'BOM' },
    { label: 'Preview' }
  ];

  activeStep = 0;

  previousStep(): void {
    if (this.activeStep > 0) {
      this.activeStep -= 1;
    }
  }

  nextStep(): void {
    if (this.activeStep < this.steps.length - 1) {
      this.activeStep += 1;
    }
  }

  goToStep(index: number): void {
    this.activeStep = index;
  }
}
