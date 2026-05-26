import { Component, ElementRef, ViewChild, AfterViewInit, HostListener, Renderer2 } from '@angular/core';

@Component({
  selector: 'app-label',
  standalone: false,
  templateUrl: './label.component.html',
  styleUrl: './label.component.scss'
})
export class LabelComponent  implements AfterViewInit {

  @ViewChild('taskContainer', { static: true }) taskContainer!: ElementRef;
  @ViewChild('linesSVG', { static: true }) linesSVG!: ElementRef<SVGElement>;
  @ViewChild('labelPage', { static: true }) labelPage!: ElementRef<HTMLElement>;
  @ViewChild('summaryLinesSVG', { static: true }) summaryLinesSVG!: ElementRef<SVGElement>;
  @ViewChild('plantBox', { static: true }) plantBox!: ElementRef<HTMLElement>;
  @ViewChild('siteBox', { static: true }) siteBox!: ElementRef<HTMLElement>;
  @ViewChild('workOrderBox', { static: true }) workOrderBox!: ElementRef<HTMLElement>;
  @ViewChild('partNumberBox', { static: true }) partNumberBox!: ElementRef<HTMLElement>;
  @ViewChild('partLeftBox', { static: true }) partLeftBox!: ElementRef<HTMLElement>;
  @ViewChild('partRightBox', { static: true }) partRightBox!: ElementRef<HTMLElement>;
  @ViewChild('productPhotoBox', { static: true }) productPhotoBox!: ElementRef<HTMLElement>;
  @ViewChild('photoLeftBox', { static: true }) photoLeftBox!: ElementRef<HTMLElement>;
  @ViewChild('photoRightBox', { static: true }) photoRightBox!: ElementRef<HTMLElement>;

  totalTasks = 37; // Total tasks
  tasksPerRow = 7; // Tasks per row
  diamondTasks = [3, 8, 14, 17, 22, 26]; // Tasks that are diamond-shaped
  diamondColors: Record<number, string> = {
    3: '#f39c12', // Orange
    8: '#f39c12'  // Purple
  };

  allNodes: HTMLElement[] = [];

  constructor(private renderer: Renderer2) {}

  ngAfterViewInit(): void {
    this.createAllElements();
    requestAnimationFrame(() => {
      this.drawLines();
      this.drawSummaryLines();
      this.highlightTask(16); // Highlight task 16 by default
    });
  }

  @HostListener('window:resize')
  onResize(): void {
    this.drawLines();
    this.drawSummaryLines();
  }

  getTaskLines(i: number): string[] {
    return [`Task ${i}`, `Line 1`, `Line 2`];
  }

  createAllElements(): void {
    const totalItems = this.totalTasks + 2; // Add packing box and shipping icons

    for (let i = 1; i <= totalItems; i++) {
      let el: HTMLElement;

      // Create icon elements for packing box and shipping finish.
      if (i > this.totalTasks) {
        el = this.renderer.createElement('div');
        this.renderer.addClass(el, 'icon');

        const symbol = this.renderer.createElement('span');
        this.renderer.addClass(symbol, 'material-symbols-outlined');
        this.renderer.addClass(symbol, 'flow-symbol');
        this.renderer.addClass(symbol, i === this.totalTasks + 1 ? 'box-symbol' : 'shipping-symbol');
        symbol.textContent = i === this.totalTasks + 1 ? 'inventory_2' : 'local_shipping';
        this.renderer.appendChild(el, symbol);
      }
      // Create task elements for the rest
      else {
        const taskNum = i;
        el = this.renderer.createElement('div');
        this.renderer.addClass(el, 'task');
        el.dataset['taskNumber'] = taskNum.toString();

        // Add diamond style to specific tasks
        if (this.diamondTasks.includes(taskNum)) {
          this.renderer.addClass(el, 'diamond');
          this.renderer.setStyle(el, 'borderColor', this.diamondColors[taskNum] || '#e94e77');
          this.renderer.setStyle(el, 'color', this.diamondColors[taskNum] || '#e94e77');
        }

        // Add text lines inside the task
        this.getTaskLines(taskNum).forEach(line => {
          const p = this.renderer.createElement('p');
          p.textContent = line;
          this.renderer.appendChild(el, p);
        });

        // Click event to update progress
        this.renderer.listen(el, 'click', () => {
          this.highlightTask(taskNum);
        });
      }

      const position = this.getFlowPosition(i);
      let row = position.row;
      let col = position.col;

      if (i === this.totalTasks + 2) {
        const boxPosition = this.getFlowPosition(this.totalTasks + 1);
        row = boxPosition.row + 1;
        col = boxPosition.col;
      }

      this.renderer.setStyle(el, 'gridRowStart', row);
      this.renderer.setStyle(el, 'gridColumnStart', col);

      this.renderer.appendChild(this.taskContainer.nativeElement, el);
      this.allNodes.push(el);
    }
  }

  getFlowPosition(sequenceNumber: number): { row: number; col: number } {
    let row = 1;
    let remaining = sequenceNumber;

    if (remaining <= 4) {
      return { row, col: remaining + 3 };
    }

    remaining -= 4;
    row++;

    while (remaining > this.tasksPerRow) {
      remaining -= this.tasksPerRow;
      row++;
    }

    const isEvenRow = row % 2 === 0;
    return {
      row,
      col: isEvenRow ? this.tasksPerRow - remaining + 1 : remaining
    };
  }

  drawLines(): void {
    const containerRect = this.taskContainer.nativeElement.getBoundingClientRect();
    const svg = this.linesSVG.nativeElement;
    svg.setAttribute('width', containerRect.width.toString());
    svg.setAttribute('height', containerRect.height.toString());
    svg.innerHTML = ''; // Clear previous lines

    const getCenter = (el: HTMLElement) => {
      const rect = el.getBoundingClientRect();
      return {
        x: rect.left + rect.width / 2 - containerRect.left,
        y: rect.top + rect.height / 2 - containerRect.top
      };
    };

    for (let i = 0; i < this.allNodes.length - 1; i++) {
      const start = getCenter(this.allNodes[i]);
      const end = getCenter(this.allNodes[i + 1]);

      const line = document.createElementNS('http://www.w3.org/2000/svg', 'line');
      line.setAttribute('x1', start.x.toString());
      line.setAttribute('y1', start.y.toString());
      line.setAttribute('x2', end.x.toString());
      line.setAttribute('y2', end.y.toString());
      line.setAttribute('stroke', 'black');
      line.setAttribute('stroke-width', '2');
      line.setAttribute('stroke-dasharray', '1,1');
      svg.appendChild(line);
    }
  }

  drawSummaryLines(): void {
    const page = this.labelPage.nativeElement;
    const pageRect = page.getBoundingClientRect();
    const svg = this.summaryLinesSVG.nativeElement;
    svg.setAttribute('width', page.scrollWidth.toString());
    svg.setAttribute('height', page.scrollHeight.toString());
    svg.innerHTML = '';

    const getCenter = (el: HTMLElement) => {
      const rect = el.getBoundingClientRect();
      return {
        x: rect.left + rect.width / 2 - pageRect.left + page.scrollLeft,
        y: rect.top + rect.height / 2 - pageRect.top + page.scrollTop
      };
    };

    const connect = (from: HTMLElement, to: HTMLElement): void => {
      const start = getCenter(from);
      const end = getCenter(to);
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

    const taskOne = this.allNodes.find((node) => node.dataset['taskNumber'] === '1');

    connect(this.plantBox.nativeElement, this.siteBox.nativeElement);
    connect(this.siteBox.nativeElement, this.workOrderBox.nativeElement);
    connect(this.workOrderBox.nativeElement, this.partNumberBox.nativeElement);
    connect(this.partLeftBox.nativeElement, this.partNumberBox.nativeElement);
    connect(this.partNumberBox.nativeElement, this.partRightBox.nativeElement);
    connect(this.partNumberBox.nativeElement, this.productPhotoBox.nativeElement);
    connect(this.photoLeftBox.nativeElement, this.productPhotoBox.nativeElement);
    connect(this.productPhotoBox.nativeElement, this.photoRightBox.nativeElement);

    if (taskOne) {
      connect(this.productPhotoBox.nativeElement, taskOne);
    }
  }

  highlightSummaryBox(target: HTMLElement): void {
    this.labelPage.nativeElement.querySelectorAll('.highlighted').forEach((el: Element) =>
      el.classList.remove('highlighted')
    );

    target.classList.remove('completed');
    target.classList.add('highlighted');
  }

  highlightTask(taskNumber_: number): void {
    // Remove previous highlights
    this.labelPage.nativeElement.querySelectorAll('.task.highlighted, .task.completed, .summary-box.completed, .product-photo.completed').forEach((el: Element) =>
      el.classList.remove('highlighted', 'completed')
    );

    if (Number(taskNumber_) > 1) {
      [
        this.plantBox.nativeElement,
        this.siteBox.nativeElement,
        this.workOrderBox.nativeElement,
        this.partLeftBox.nativeElement,
        this.partNumberBox.nativeElement,
        this.partRightBox.nativeElement,
        this.photoLeftBox.nativeElement,
        this.productPhotoBox.nativeElement,
        this.photoRightBox.nativeElement
      ].forEach((el) => el.classList.add('completed'));
    }

    //Highlight the current task and mark previous ones as completed
    this.allNodes.forEach((el, i) => {
      const taskNumber = el.dataset['taskNumber'];
      const num = taskNumber ? parseInt(taskNumber, 10) : NaN;

      if (num < Number(taskNumber_)) {
        el.classList.add('completed');
      } else if (num === Number(taskNumber_)) {
        el.classList.add('highlighted');
      }
    });


    //  const taskElement = this.allNodes[taskNumber_];
    // if (taskElement?.classList.contains('task')) {
    //   taskElement.classList.add('highlighted');
    // }

  }
}
