import { Directive, Input, OnChanges, SimpleChanges } from '@angular/core';

@Directive()
export abstract class ReportViewBridge implements OnChanges {
  @Input({ required: true }) vm!: any;
  [key: string]: any;

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['vm']) {
      this.bindViewModel();
    }
  }

  protected bindViewModel(): void {
    if (!this.vm) {
      return;
    }

    this.bindObjectKeys(this.vm);
    let prototype = Object.getPrototypeOf(this.vm);
    while (prototype && prototype !== Object.prototype) {
      this.bindObjectKeys(prototype);
      prototype = Object.getPrototypeOf(prototype);
    }
  }

  private bindObjectKeys(source: any): void {
    Object.getOwnPropertyNames(source).forEach((key) => {
      if (key === 'constructor' || key in this) {
        return;
      }

      const descriptor = Object.getOwnPropertyDescriptor(source, key);
      if (!descriptor) {
        return;
      }

      if (typeof descriptor.value === 'function') {
        Object.defineProperty(this, key, {
          configurable: true,
          get: () => this.vm[key].bind(this.vm),
        });
        return;
      }

      Object.defineProperty(this, key, {
        configurable: true,
        get: () => this.vm[key],
        set: (value) => {
          this.vm[key] = value;
        },
      });
    });
  }
}
