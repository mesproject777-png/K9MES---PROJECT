import { Component } from '@angular/core';

import { ReportViewBridge } from '../report-view-bridge';

@Component({
  selector: 'app-debug-dashboard',
  standalone: false,
  templateUrl: './debug-dashboard.component.html',
})
export class DebugDashboardComponent extends ReportViewBridge {}
