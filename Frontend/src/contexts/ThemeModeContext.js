import { createContext, useContext } from 'react';

export const ThemeModeContext = createContext({
  mode: 'light',
  setMode: () => {},
  toggleMode: () => {},
});

export const useThemeMode = () => useContext(ThemeModeContext);

