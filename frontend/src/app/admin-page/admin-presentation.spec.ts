import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { AdminEditorFocus } from './admin-editor-focus';
import { AdminFailureReason } from './admin-failure-reason';

@Component({
  imports: [AdminEditorFocus],
  template: `<button id="opener" (click)="open.set(true)">Edit data</button>
    @if (open()) {
      <section adminEditorFocus role="dialog">
        <button id="first">Close</button><input aria-label="Amount" />
        <button id="disabled" disabled>Unavailable</button><button id="last">Save</button>
      </section>
    }`,
})
class EditorHarness {
  readonly open = signal(false);
}

describe('Admin presentation', () => {
  const pipe = new AdminFailureReason();
  it('unwraps an API message without rewriting its meaning', () => {
    expect(pipe.transform('{"message":"\\u0027Caffeine\\u0027: invalid unit"}')).toBe(
      "'Caffeine': invalid unit",
    );
  });
  it('preserves plain text and structured responses without a message', () => {
    expect(pipe.transform('Product not found.')).toBe('Product not found.');
    expect(pipe.transform('{"error":"Unknown"}')).toBe('{"error":"Unknown"}');
    expect(pipe.transform('{"message":23}')).toBe('{"message":23}');
    expect(pipe.transform(null)).toBe('—');
  });
  it('renders unsafe-looking messages as strings, not markup', () => {
    expect(pipe.transform('{"message":"<img src=x>"}')).toBe('<img src=x>');
  });
  it('moves focus inside, contains Tab and restores focus and scrolling on close', async () => {
    const fixture = TestBed.createComponent(EditorHarness);
    fixture.detectChanges();
    const opener: HTMLButtonElement = fixture.nativeElement.querySelector('#opener');
    opener.focus();
    const previousOverflow = document.body.style.overflow;
    opener.click();
    fixture.detectChanges();
    await fixture.whenStable();
    const dialog: HTMLElement = fixture.nativeElement.querySelector('[role="dialog"]');
    const first = dialog.querySelector<HTMLElement>('#first')!;
    const last = dialog.querySelector<HTMLElement>('#last')!;
    expect(document.activeElement).toBe(dialog);
    expect(document.body.style.overflow).toBe('hidden');
    dialog.dispatchEvent(
      new KeyboardEvent('keydown', { key: 'Tab', bubbles: true, cancelable: true }),
    );
    expect(document.activeElement).toBe(first);
    first.dispatchEvent(
      new KeyboardEvent('keydown', { key: 'Tab', shiftKey: true, bubbles: true, cancelable: true }),
    );
    expect(document.activeElement).toBe(last);
    last.dispatchEvent(
      new KeyboardEvent('keydown', { key: 'Tab', bubbles: true, cancelable: true }),
    );
    expect(document.activeElement).toBe(first);
    fixture.componentInstance.open.set(false);
    fixture.detectChanges();
    await fixture.whenStable();
    expect(document.activeElement).toBe(opener);
    expect(document.body.style.overflow).toBe(previousOverflow);
    fixture.destroy();
  });
});
