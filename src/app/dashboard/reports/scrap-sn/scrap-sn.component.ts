import { Component } from '@angular/core';

import { ReportViewBridge } from '../report-view-bridge';

@Component({
  selector: 'app-scrap-sn',
  standalone: false,
  templateUrl: './scrap-sn.component.html',
})
export class ScrapSnComponent extends ReportViewBridge {}
