import { normalizeStoredToken } from '../services/api';

describe('normalizeStoredToken', () => {
  it('returns null for empty values', () => {
    expect(normalizeStoredToken(null)).toBeNull();
    expect(normalizeStoredToken(undefined)).toBeNull();
    expect(normalizeStoredToken('')).toBeNull();
    expect(normalizeStoredToken('   ')).toBeNull();
    expect(normalizeStoredToken('null')).toBeNull();
    expect(normalizeStoredToken('undefined')).toBeNull();
  });

  it('trims and unwraps quotes', () => {
    expect(normalizeStoredToken('"abc.def.ghi"')).toBe('abc.def.ghi');
    expect(normalizeStoredToken('  "xyz"  ')).toBe('xyz');
  });

  it('normalizes Bearer prefix', () => {
    expect(normalizeStoredToken('Bearer abc.def')).toBe('abc.def');
    expect(normalizeStoredToken('bearer token-value')).toBe('token-value');
  });
});
