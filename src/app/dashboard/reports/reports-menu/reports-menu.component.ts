import { Component } from '@angular/core';

import { ReportViewBridge } from '../report-view-bridge';

@Component({
  selector: 'app-reports-menu',
  standalone: false,
  templateUrl: './reports-menu.component.html',
})
export class ReportsMenuComponent extends ReportViewBridge {}
