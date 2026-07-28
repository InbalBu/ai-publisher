import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { ThemeProvider, createTheme, CssBaseline } from '@mui/material'
import App from './App'

// direction: 'rtl' flips MUI's own layout logic (drawer sides, icon margins,
// etc). Pairing it with dir="rtl" on <html> (set in index.html) covers native
// text direction. That is enough for a single internal form; the full
// stylis-plugin-rtl emotion cache is only worth it once the UI has more than
// one screen's worth of components to mirror.
const theme = createTheme({
  direction: 'rtl',
  palette: { mode: 'light' },
})

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <ThemeProvider theme={theme}>
      <CssBaseline />
      <App />
    </ThemeProvider>
  </StrictMode>,
)
