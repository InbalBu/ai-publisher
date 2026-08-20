import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { ThemeProvider, createTheme, CssBaseline } from '@mui/material'
import { CacheProvider } from '@emotion/react'
import createCache from '@emotion/cache'
import { prefixer } from 'stylis'
import rtlPlugin from 'stylis-plugin-rtl'
import App from './App'

// A real stylis-level RTL cache, not just theme.direction + dir="rtl": those
// two only flip MUI's own internal layout logic (Grid, Drawer, icon
// margins). Any physical CSS this app's own sx props write - marginRight,
// left/right positioning for badges and overlays, borderRight accents -
// would otherwise render mirrored on screen instead of flipped, since the
// browser has no idea "right" was meant to mean "the far edge" in an RTL
// reading direction. This cache rewrites every rule stylis emits (via
// stylis-plugin-rtl) so physical properties are flipped automatically,
// the same mechanism MUI's own official RTL guide uses.
const cacheRtl = createCache({
  key: 'muirtl',
  stylisPlugins: [prefixer, rtlPlugin],
})

const theme = createTheme({
  direction: 'rtl',
  palette: {
    mode: 'light',
    primary: { main: '#1a56db' },
    secondary: { main: '#d81b60' },
    background: { default: '#f4f6fb' },
  },
  shape: { borderRadius: 14 },
  typography: {
    fontFamily: '"Assistant", "Rubik", "Segoe UI", Arial, sans-serif',
    h4: { fontWeight: 700 },
    h5: { fontWeight: 700 },
  },
  components: {
    MuiPaper: {
      styleOverrides: {
        root: { backgroundImage: 'none' },
      },
    },
    MuiButton: {
      styleOverrides: {
        root: { borderRadius: 10, textTransform: 'none', fontWeight: 600 },
      },
    },
    MuiTextField: {
      defaultProps: { variant: 'outlined' },
    },
    MuiOutlinedInput: {
      styleOverrides: {
        root: { borderRadius: 10 },
      },
    },
  },
})

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <CacheProvider value={cacheRtl}>
      <ThemeProvider theme={theme}>
        <CssBaseline />
        <App />
      </ThemeProvider>
    </CacheProvider>
  </StrictMode>,
)
