import { Component } from '@angular/core';

import { ReportViewBridge } from '../report-view-bridge';

@Component({
  selector: 'app-activity-quality-dashboard',
  standalone: false,
  templateUrl: './activity-quality-dashboard.component.html',
})
export class ActivityQualityDashboardComponent extends ReportViewBridge {}
