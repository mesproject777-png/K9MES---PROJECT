import { AfterViewInit, Component, ElementRef, Renderer2, ViewChild } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { ActivatedRoute } from '@angular/router';
import { AuthService, AuthUser } from '../../services/auth.service';

interface LabelItem {
  id: number;
  pn: string;
  description: string;
}

interface LabelStepRow {
  station_code: string;
  station_name: string;
  sample_mode: string;
  report_mode: string;
  station_order: number;
}

interface LabelRoutingResponse {
  item: LabelItem;
  data: LabelStepRow[];
  total: number;
}

interface DiagramNode {
  lines: string[];
  isDiamond?: boolean;
  highlight?: boolean;
}

@Component({
  selector: 'app-label',
  standalone: false,
  templateUrl: './label.component.html',
  styleUrl: './label.component.scss'
})
export class LabelComponent implements AfterViewInit {
  @ViewChild('taskContainer', { static: true }) taskContainer!: ElementRef;
  @ViewChild('linesSVG', { static: true }) linesSVG!: ElementRef<SVGElement>;

  readonly routingApi = 'http://localhost:5000/api/routing';
  readonly defaultPn = 'blb-stb';
  readonly tasksPerRow = 8;
  readonly diamondTasks = [3, 8, 14, 17, 22, 26];
  readonly diamondColors: Record<number, string> = {
    3: '#f39c12',
    8: '#f39c12'
  };

  currentPn = this.defaultPn;
  selectedItem: LabelItem | null = null;
  routeSteps: LabelStepRow[] = [];
  currentUser: AuthUser | null = null;
  isLoading = false;
  statusMessage = '';
  allNodes: HTMLElement[] = [];
  allRows: HTMLElement[][] = [];
  hierarchyNodes: Record<string, HTMLElement> = {};

  private readonly iconImages = {
    person: 'https://cdn-icons-png.flaticon.com/512/1839/1839365.png',
    truck: 'https://cdn-icons-png.flaticon.com/512/10849/10849258.png'
  };

  constructor(
    private renderer: Renderer2,
    private http: HttpClient,
    private route: ActivatedRoute,
    private authService: AuthService
  ) {}

  ngAfterViewInit(): void {
    this.currentUser = this.authService.getCurrentUser();
    this.renderDemoFlow();
    this.route.queryParamMap.subscribe((params) => {
      const pn = params.get('pn')?.trim() || this.defaultPn;
      this.currentPn = pn;
      this.loadRoutingForCurrentPn();
    });
  }

  private loadRoutingForCurrentPn(): void {
    this.isLoading = true;
    this.statusMessage = '';

    const params = new HttpParams().set('pn', this.currentPn);
    this.http.get<LabelItem>(`${this.routingApi}/by-pn`, { params }).subscribe({
      next: (item) => {
        this.selectedItem = item;
        this.loadRoutingSteps(item.id);
      },
      error: () => {
        this.selectedItem = null;
        this.routeSteps = [];
        this.isLoading = false;
        this.statusMessage = `No routing found for ${this.currentPn}. Open Routing to add one.`;
        this.renderDemoFlow();
      }
    });
  }

  private loadRoutingSteps(itemId: number): void {
    const params = new HttpParams().set('includeHistory', 'false');
    this.http.get<LabelRoutingResponse>(`${this.routingApi}/${itemId}/steps`, { params }).subscribe({
      next: (response) => {
        this.routeSteps = response.data || [];
        this.isLoading = false;

        if (this.routeSteps.length === 0) {
          this.statusMessage = `No route steps are configured for ${this.currentPn}.`;
          this.renderDemoFlow();
          return;
        }

        this.statusMessage = `${this.routeSteps.length} route step(s) loaded for ${this.currentPn}.`;
        this.renderRouteFlow();
      },
      error: () => {
        this.isLoading = false;
        this.statusMessage = `Unable to load route details for ${this.currentPn}.`;
        this.renderDemoFlow();
      }
    });
  }

  private renderDemoFlow(): void {
    const nodes = Array.from({ length: 37 }, (_, index) => ({
      lines: [`Task ${index + 1}`, 'Line 1', 'Line 2'],
      isDiamond: this.diamondTasks.includes(index + 1),
      highlight: index + 1 === 16
    }));

    this.renderDiagram(nodes);
  }

  private renderRouteFlow(): void {
    const nodes = this.routeSteps.map((step, index) => ({
      lines: [step.station_name],
      isDiamond: step.sample_mode === 'Sample',
      highlight: index === 0
    }));
    this.renderDiagram(nodes);
  }

  private renderDiagram(nodes: DiagramNode[]): void {
    this.allNodes = [];
    this.allRows = [];
    this.hierarchyNodes = {};
    this.taskContainer.nativeElement.innerHTML = '';
    this.linesSVG.nativeElement.innerHTML = '';

    // Group nodes into rows
    const itemsPerRow = this.tasksPerRow;
    const nodeElements: HTMLElement[] = [];
    // Create start icon
    const startIcon = this.renderer.createElement('div');
    this.renderer.addClass(startIcon, 'icon');
    const startImg = this.renderer.createElement('img');
    startImg.src = this.iconImages.person;
    startImg.alt = 'Person';
    this.renderer.appendChild(startIcon, startImg);
    nodeElements.push(startIcon);
    // Create task nodes
    for (let i = 0; i < nodes.length; i++) {
      const node = nodes[i];
      const taskNum = i + 1;
      const el = this.renderer.createElement('div');
      this.renderer.addClass(el, 'task');
      el.dataset['taskNumber'] = taskNum.toString();
      if (node.isDiamond) {
        this.renderer.addClass(el, 'diamond');
      }
      const p = this.renderer.createElement('p');
      p.textContent = node.lines[0];
      this.renderer.appendChild(el, p);
      this.renderer.listen(el, 'click', () => {
        this.highlightTask(taskNum);
      });
      nodeElements.push(el);
    }
    // Create end icon
    const endIcon = this.renderer.createElement('div');
    this.renderer.addClass(endIcon, 'icon');
    const endImg = this.renderer.createElement('img');
    endImg.src = this.iconImages.truck;
    endImg.alt = 'Truck';
    this.renderer.appendChild(endIcon, endImg);
    nodeElements.push(endIcon);

    // Group into rows
    let row: HTMLElement[] = [];
    for (let i = 0; i < nodeElements.length; i++) {
      row.push(nodeElements[i]);
      // Row break: first row includes start icon, then every itemsPerRow, last row includes end icon
      const isLast = i === nodeElements.length - 1;
      if ((row.length === itemsPerRow) || isLast) {
        this.allRows.push(row);
        row = [];
      }
    }

    this.renderHierarchySection();

    // Render rows as a snake flow: left-to-right, then right-to-left.
    this.allRows.forEach((rowNodes, rowIndex) => {
      const rowDiv = this.renderer.createElement('div');
      this.renderer.addClass(rowDiv, 'diagram-row');
      this.renderer.setStyle(rowDiv, 'display', 'flex');
      this.renderer.setStyle(rowDiv, 'justifyContent', 'center');
      this.renderer.setStyle(rowDiv, 'alignItems', 'center');
      this.renderer.setStyle(rowDiv, 'gap', '24px');
      const visualRowNodes = rowIndex % 2 === 0 ? rowNodes : [...rowNodes].reverse();
      const placeholderCount = Math.max(itemsPerRow - rowNodes.length, 0);
      const placeholders = Array.from({ length: placeholderCount }, () => this.createFlowPlaceholder());
      const visualItems = rowIndex % 2 === 0
        ? [...visualRowNodes, ...placeholders]
        : [...placeholders, ...visualRowNodes];
      visualItems.forEach((el) => {
        this.renderer.appendChild(rowDiv, el);
      });
      rowNodes.forEach((el) => this.allNodes.push(el));
      this.renderer.appendChild(this.taskContainer.nativeElement, rowDiv);
    });

    requestAnimationFrame(() => {
      this.drawLines();
      if (this.routeSteps.length > 0) {
        this.highlightTask(1);
      } else {
        this.highlightTask(16);
      }
    });
  }

  private renderHierarchySection(): void {
    const hierarchyRows = [
      ['plant'],
      ['bits'],
      ['workOrder'],
      ['parentPartNo', 'partNumber', 'child'],
      ['boxLeft', 'productPhoto', 'boxRight']
    ];

    const labels: Record<string, string> = {
      plant: 'Plant',
      bits: 'Site',
      workOrder: 'Work Order',
      parentPartNo: 'Parent Part No',
      partNumber: 'Part Number',
      child: 'Child',
      boxLeft: 'Box',
      productPhoto: 'Product Photo',
      boxRight: 'Box'
    };

    const stepNumbers: Record<string, number> = {
      plant: -9,
      bits: -8,
      workOrder: -7,
      parentPartNo: -6,
      child: -5,
      partNumber: -4,
      boxLeft: -3,
      boxRight: -2,
      productPhoto: 0
    };

    const hierarchySection = this.renderer.createElement('div');
    this.renderer.addClass(hierarchySection, 'hierarchy-flow');

    hierarchyRows.forEach((rowKeys) => {
      const rowDiv = this.renderer.createElement('div');
      this.renderer.addClass(rowDiv, 'hierarchy-row');

      rowKeys.forEach((key) => {
        const box = this.renderer.createElement('div');
        this.renderer.addClass(box, 'hierarchy-box');
        box.dataset['taskNumber'] = stepNumbers[key].toString();
        box.textContent = labels[key];
        this.renderer.listen(box, 'click', () => {
          this.highlightTask(stepNumbers[key]);
        });
        this.hierarchyNodes[key] = box;
        this.renderer.appendChild(rowDiv, box);
      });

      this.renderer.appendChild(hierarchySection, rowDiv);
    });

    this.renderer.appendChild(this.taskContainer.nativeElement, hierarchySection);
    Object.values(this.hierarchyNodes).forEach((node) => this.allNodes.push(node));
  }

  private createFlowPlaceholder(): HTMLElement {
    const placeholder = this.renderer.createElement('div');
    this.renderer.setStyle(placeholder, 'width', '100px');
    this.renderer.setStyle(placeholder, 'minWidth', '100px');
    this.renderer.setStyle(placeholder, 'height', '120px');
    this.renderer.setStyle(placeholder, 'visibility', 'hidden');
    this.renderer.setStyle(placeholder, 'pointerEvents', 'none');
    return placeholder;
  }

  drawLines(): void {
    const containerRect = this.taskContainer.nativeElement.getBoundingClientRect();
    const svg = this.linesSVG.nativeElement;
    svg.setAttribute('width', containerRect.width.toString());
    svg.setAttribute('height', containerRect.height.toString());
    svg.innerHTML = '';

    const getCenter = (el: HTMLElement) => {
      const rect = el.getBoundingClientRect();
      return {
        x: rect.left + rect.width / 2 - containerRect.left,
        y: rect.top + rect.height / 2 - containerRect.top
      };
    };

    const drawLine = (start: { x: number; y: number }, end: { x: number; y: number }): void => {
      const line = document.createElementNS('http://www.w3.org/2000/svg', 'line');
      line.setAttribute('x1', start.x.toString());
      line.setAttribute('y1', start.y.toString());
      line.setAttribute('x2', end.x.toString());
      line.setAttribute('y2', end.y.toString());
      line.setAttribute('stroke', 'black');
      line.setAttribute('stroke-width', '2');
      line.setAttribute('stroke-dasharray', '1,1');
      svg.appendChild(line);
    };

    const getBottomCenter = (el: HTMLElement) => {
      const rect = el.getBoundingClientRect();
      return {
        x: rect.left + rect.width / 2 - containerRect.left,
        y: rect.bottom - containerRect.top
      };
    };

    const getTopCenter = (el: HTMLElement) => {
      const rect = el.getBoundingClientRect();
      return {
        x: rect.left + rect.width / 2 - containerRect.left,
        y: rect.top - containerRect.top
      };
    };

    const getLeftCenter = (el: HTMLElement) => {
      const rect = el.getBoundingClientRect();
      return {
        x: rect.left - containerRect.left,
        y: rect.top + rect.height / 2 - containerRect.top
      };
    };

    const getRightCenter = (el: HTMLElement) => {
      const rect = el.getBoundingClientRect();
      return {
        x: rect.right - containerRect.left,
        y: rect.top + rect.height / 2 - containerRect.top
      };
    };

    const drawOrthogonal = (from: HTMLElement, to: HTMLElement): void => {
      const start = getBottomCenter(from);
      const end = getTopCenter(to);
      const midY = start.y + Math.max((end.y - start.y) / 2, 12);
      drawLine(start, { x: start.x, y: midY });
      drawLine({ x: start.x, y: midY }, { x: end.x, y: midY });
      drawLine({ x: end.x, y: midY }, end);
    };

    const drawHorizontal = (start: { x: number; y: number }, end: { x: number; y: number }): void => {
      drawLine(start, end);
    };

    const hierarchy = this.hierarchyNodes;
    if (hierarchy['plant'] && hierarchy['bits']) {
      drawOrthogonal(hierarchy['plant'], hierarchy['bits']);
    }
    if (hierarchy['bits'] && hierarchy['workOrder']) {
      drawOrthogonal(hierarchy['bits'], hierarchy['workOrder']);
    }
    if (hierarchy['workOrder'] && hierarchy['partNumber']) {
      drawOrthogonal(hierarchy['workOrder'], hierarchy['partNumber']);
    }
    if (hierarchy['parentPartNo'] && hierarchy['partNumber']) {
      drawHorizontal(getRightCenter(hierarchy['parentPartNo']), getLeftCenter(hierarchy['partNumber']));
    }
    if (hierarchy['child'] && hierarchy['partNumber']) {
      drawHorizontal(getLeftCenter(hierarchy['child']), getRightCenter(hierarchy['partNumber']));
    }
    if (hierarchy['partNumber'] && hierarchy['productPhoto']) {
      drawOrthogonal(hierarchy['partNumber'], hierarchy['productPhoto']);
    }
    if (hierarchy['boxLeft'] && hierarchy['productPhoto']) {
      drawHorizontal(getRightCenter(hierarchy['boxLeft']), getLeftCenter(hierarchy['productPhoto']));
    }
    if (hierarchy['boxRight'] && hierarchy['productPhoto']) {
      drawHorizontal(getLeftCenter(hierarchy['boxRight']), getRightCenter(hierarchy['productPhoto']));
    }
    if (hierarchy['productPhoto'] && this.allRows[0]?.[0]) {
      drawOrthogonal(hierarchy['productPhoto'], this.allRows[0][0]);
    }

    // Draw lines within each row
    for (let r = 0; r < this.allRows.length; r++) {
      const row = this.allRows[r];
      for (let i = 0; i < row.length - 1; i++) {
        const start = getCenter(row[i]);
        const end = getCenter(row[i + 1]);
        drawLine(start, end);
      }
      // Draw a short vertical connection from each row end down to the next row start.
      if (r > 0 && this.allRows[r - 1].length > 0 && row.length > 0) {
        const prevRow = this.allRows[r - 1];
        const start = getCenter(prevRow[prevRow.length - 1]);
        const end = getCenter(row[0]);
        drawLine(start, end);
      }
    }
  }

  highlightTask(taskNumber_: number): void {
    this.taskContainer.nativeElement.querySelectorAll(
      '.task.highlighted, .task.completed, .hierarchy-box.highlighted, .hierarchy-box.completed'
    ).forEach((el: HTMLElement) => {
      el.classList.remove('highlighted', 'completed');
    });

    this.allNodes.forEach((el) => {
      const taskNumber = el.dataset['taskNumber'];
      const num = taskNumber ? parseInt(taskNumber, 10) : NaN;

      if (num < Number(taskNumber_)) {
        el.classList.add('completed');
      } else if (num === Number(taskNumber_)) {
        el.classList.add('highlighted');
      }
    });
  }
}
