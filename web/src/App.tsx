import { useEffect, useState } from 'react'
import {
  Alert,
  Box,
  Button,
  CircularProgress,
  Container,
  FormControlLabel,
  IconButton,
  MenuItem,
  Paper,
  Radio,
  RadioGroup,
  Stack,
  TextField,
  Typography,
} from '@mui/material'
import { checkSession, fetchCategories, logout, publishArticle } from './api'
import type { Category, PublishResult } from './api'
import LoginPage from './LoginPage'

interface ImageEntry {
  file: File
  previewUrl: string
  /** Photo credit, e.g. "צילום: ישראל ישראלי". Blank means no caption is added. */
  caption: string
}

function App() {
  // undefined = still checking, null = not logged in, object = logged in
  const [session, setSession] = useState<{ username: string } | null | undefined>(undefined)
  const [categories, setCategories] = useState<Category[]>([])
  const [rawText, setRawText] = useState('')
  const [useAi, setUseAi] = useState(true)
  const [title, setTitle] = useState('')
  const [subtitle, setSubtitle] = useState('')
  const [categoryId, setCategoryId] = useState<number | ''>('')
  const [images, setImages] = useState<ImageEntry[]>([])
  const [featuredIndex, setFeaturedIndex] = useState(0)
  const [submitting, setSubmitting] = useState<'publish' | 'draft' | null>(null)
  const [result, setResult] = useState<PublishResult | null>(null)

  useEffect(() => {
    checkSession().then(setSession).catch(() => setSession(null))
  }, [])

  useEffect(() => {
    if (session) {
      fetchCategories().then(setCategories).catch(() => setCategories([]))
    }
  }, [session])

  useEffect(() => {
    // Release object URLs on unmount only; per-change cleanup happens in removeImage.
    return () => images.forEach((image) => URL.revokeObjectURL(image.previewUrl))
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  function addImages(fileList: FileList | null) {
    if (!fileList) return
    const added = Array.from(fileList).map((file) => ({
      file,
      previewUrl: URL.createObjectURL(file),
      caption: '',
    }))
    setImages((prev) => [...prev, ...added])
  }

  function updateCaption(index: number, caption: string) {
    setImages((prev) => prev.map((image, i) => (i === index ? { ...image, caption } : image)))
  }

  function removeImage(index: number) {
    setImages((prev) => {
      URL.revokeObjectURL(prev[index].previewUrl)
      return prev.filter((_, i) => i !== index)
    })
    setFeaturedIndex((prev) => {
      if (index === prev) return 0
      return index < prev ? prev - 1 : prev
    })
  }

  const canSubmit =
    rawText.trim().length >= 50 &&
    categoryId !== '' &&
    images.length > 0 &&
    submitting === null &&
    (useAi || (title.trim().length > 0 && subtitle.trim().length > 0))

  async function handleSubmit(status: 'publish' | 'draft') {
    if (!canSubmit) return

    setSubmitting(status)
    setResult(null)
    try {
      const outcome = await publishArticle({
        rawText,
        categoryId,
        status,
        featuredImageIndex: featuredIndex,
        images: images.map((i) => i.file),
        captions: images.map((i) => i.caption),
        useAi,
        title,
        subtitle,
      })
      setResult(outcome)
    } catch (err) {
      setResult({
        success: false,
        elapsedMs: 0,
        error: err instanceof Error ? err.message : 'שגיאה לא ידועה',
      })
    } finally {
      setSubmitting(null)
    }
  }

  if (session === undefined) {
    return (
      <Box sx={{ display: 'flex', justifyContent: 'center', pt: 12 }}>
        <CircularProgress />
      </Box>
    )
  }

  if (session === null) {
    return <LoginPage onSuccess={() => checkSession().then(setSession)} />
  }

  async function handleLogout() {
    await logout()
    setSession(null)
  }

  return (
    <Container maxWidth="md" sx={{ py: 5 }}>
      <Stack direction="row" sx={{ alignItems: 'center', justifyContent: 'space-between', mb: 1 }}>
        <Typography variant="h4" component="h1" gutterBottom sx={{ mb: 0 }}>
          פרסום כתבה
        </Typography>
        <Stack direction="row" sx={{ alignItems: 'center' }} spacing={1}>
          <Typography variant="body2" color="text.secondary">
            {session.username}
          </Typography>
          <Button size="small" onClick={handleLogout}>
            התנתקות
          </Button>
        </Stack>
      </Stack>
      <Typography variant="body2" color="text.secondary" sx={{ mb: 3 }}>
        הדביקו טקסט גולמי, בחרו קטגוריה, העלו תמונות וסמנו תמונה ראשית.
      </Typography>

      <Paper sx={{ p: 3 }}>
        <Stack spacing={3}>
          <TextField
            label="טקסט הכתבה"
            multiline
            minRows={10}
            value={rawText}
            onChange={(e) => setRawText(e.target.value)}
            fullWidth
          />

          <Box>
            <Typography variant="body2" sx={{ mb: 0.5 }}>
              עיבוד הכתבה
            </Typography>
            <RadioGroup
              row
              value={useAi ? 'ai' : 'manual'}
              onChange={(e) => setUseAi(e.target.value === 'ai')}
            >
              <FormControlLabel value="ai" control={<Radio />} label="עם AI" />
              <FormControlLabel value="manual" control={<Radio />} label="בלי AI" />
            </RadioGroup>
          </Box>

          {!useAi && (
            <>
              <TextField
                label="כותרת ראשית"
                value={title}
                onChange={(e) => setTitle(e.target.value)}
                fullWidth
              />
              <TextField
                label="כותרת משנה"
                multiline
                minRows={2}
                value={subtitle}
                onChange={(e) => setSubtitle(e.target.value)}
                fullWidth
              />
            </>
          )}

          <TextField
            select
            label="קטגוריה"
            value={categoryId}
            onChange={(e) => setCategoryId(Number(e.target.value))}
            fullWidth
          >
            {categories.map((c) => (
              <MenuItem key={c.id} value={c.id}>
                {c.name}
              </MenuItem>
            ))}
          </TextField>

          <Box>
            <Button component="label" variant="outlined">
              העלאת תמונות
              <input
                type="file"
                accept="image/*"
                multiple
                hidden
                onChange={(e) => addImages(e.target.files)}
              />
            </Button>

            {images.length > 0 && (
              <Box
                sx={{
                  mt: 2,
                  display: 'grid',
                  gridTemplateColumns: 'repeat(auto-fill, minmax(180px, 1fr))',
                  gap: 2,
                }}
              >
                {images.map((image, index) => (
                  <Paper key={image.previewUrl} variant="outlined" sx={{ p: 1, textAlign: 'center' }}>
                    <Box
                      component="img"
                      src={image.previewUrl}
                      alt=""
                      sx={{ width: '100%', height: 100, objectFit: 'cover', borderRadius: 1 }}
                    />
                    <Stack
                      direction="row"
                      sx={{ mt: 0.5, alignItems: 'center', justifyContent: 'space-between' }}
                    >
                      <Stack direction="row" sx={{ alignItems: 'center' }}>
                        <Radio
                          size="small"
                          checked={featuredIndex === index}
                          onChange={() => setFeaturedIndex(index)}
                        />
                        <Typography variant="caption">ראשית</Typography>
                      </Stack>
                      <IconButton size="small" onClick={() => removeImage(index)} aria-label="הסרת תמונה">
                        ✕
                      </IconButton>
                    </Stack>
                    <TextField
                      size="small"
                      fullWidth
                      placeholder="קרדיט לתמונה (אופציונלי)"
                      value={image.caption}
                      onChange={(e) => updateCaption(index, e.target.value)}
                      sx={{ mt: 1 }}
                    />
                  </Paper>
                ))}
              </Box>
            )}
          </Box>

          <Stack direction="row" spacing={2}>
            <Button variant="outlined" disabled={!canSubmit} onClick={() => handleSubmit('draft')}>
              {submitting === 'draft' ? 'שומר טיוטה...' : 'שמירת טיוטה'}
            </Button>
            <Button variant="contained" disabled={!canSubmit} onClick={() => handleSubmit('publish')}>
              {submitting === 'publish' ? 'מפרסם...' : 'פרסום'}
            </Button>
          </Stack>
        </Stack>
      </Paper>

      {result && (
        <Alert severity={result.success ? 'success' : 'error'} sx={{ mt: 3 }}>
          {result.success ? (
            <>
              פורסם בהצלחה: <strong>{result.title}</strong> ({(result.elapsedMs / 1000).toFixed(1)} שניות)
              {result.url && (
                <>
                  {' - '}
                  <a href={result.url} target="_blank" rel="noreferrer">
                    צפייה בכתבה
                  </a>
                </>
              )}
            </>
          ) : (
            <>הפרסום נכשל: {result.error}</>
          )}
        </Alert>
      )}
    </Container>
  )
}

export default App
