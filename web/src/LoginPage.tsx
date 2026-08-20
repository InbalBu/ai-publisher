import { useState } from 'react'
import { Alert, Avatar, Box, Button, Container, Paper, Stack, TextField, Typography } from '@mui/material'
import { login } from './api'

export default function LoginPage({ onSuccess }: { onSuccess: () => void }) {
  const [username, setUsername] = useState('')
  const [password, setPassword] = useState('')
  const [submitting, setSubmitting] = useState(false)
  const [error, setError] = useState<string | null>(null)

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault()
    setSubmitting(true)
    setError(null)
    try {
      const ok = await login(username, password)
      if (ok) {
        onSuccess()
      } else {
        setError('שם משתמש או סיסמה שגויים.')
      }
    } catch (err) {
      setError(err instanceof Error ? err.message : 'שגיאה לא ידועה.')
    } finally {
      setSubmitting(false)
    }
  }

  return (
    <Box
      sx={{
        minHeight: '100dvh',
        display: 'flex',
        alignItems: 'center',
        background: 'linear-gradient(135deg, #1a56db 0%, #1741a6 100%)',
      }}
    >
      <Container maxWidth="xs">
        <Stack spacing={2} sx={{ alignItems: 'center', mb: 2 }}>
          <Avatar sx={{ bgcolor: 'common.white', color: 'primary.main', width: 56, height: 56, fontSize: 26 }}>
            ✍️
          </Avatar>
          <Typography variant="h5" component="h1" sx={{ color: 'common.white', fontWeight: 700 }}>
            מקומון AI Publisher
          </Typography>
        </Stack>
        <Paper
          sx={{
            p: { xs: 3, sm: 4 },
            borderRadius: 3,
            boxShadow: '0 16px 40px rgba(10, 25, 60, 0.25)',
          }}
        >
          <Typography variant="h6" component="h2" sx={{ mb: 2, fontWeight: 700, textAlign: 'center' }}>
            התחברות
          </Typography>
          <Box component="form" onSubmit={handleSubmit}>
            <Stack spacing={2}>
              <TextField
                label="שם משתמש"
                value={username}
                onChange={(e) => setUsername(e.target.value)}
                autoFocus
                fullWidth
              />
              <TextField
                label="סיסמה"
                type="password"
                value={password}
                onChange={(e) => setPassword(e.target.value)}
                fullWidth
              />
              {error && (
                <Alert severity="error" sx={{ borderRadius: 2 }}>
                  {error}
                </Alert>
              )}
              <Button
                type="submit"
                variant="contained"
                size="large"
                disabled={submitting || !username || !password}
              >
                {submitting ? 'מתחבר...' : 'התחברות'}
              </Button>
            </Stack>
          </Box>
        </Paper>
      </Container>
    </Box>
  )
}
