import { afterEach, describe, expect, it, vi } from 'vitest';
import { downloadBlob } from './downloadBlob';

describe('downloadBlob', () => {
  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('creates and revokes object URL', () => {
    const createObjectURL = vi.fn(() => 'blob:test');
    const revokeObjectURL = vi.fn();
    vi.stubGlobal('URL', { createObjectURL, revokeObjectURL });

    const click = vi.fn();
    const remove = vi.fn();
    const anchor = document.createElement('a');
    anchor.click = click;
    anchor.remove = remove;
    const createElement = vi.spyOn(document, 'createElement').mockReturnValue(anchor);
    const appendChild = vi.spyOn(document.body, 'appendChild').mockImplementation(() => anchor);

    downloadBlob(new Blob(['x']), 'file.txt');

    expect(createObjectURL).toHaveBeenCalled();
    expect(click).toHaveBeenCalled();
    expect(remove).toHaveBeenCalled();
    expect(revokeObjectURL).toHaveBeenCalledWith('blob:test');
    createElement.mockRestore();
    appendChild.mockRestore();
  });
});
