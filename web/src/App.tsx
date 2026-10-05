import { useEffect, useState } from 'react'
import type { DragEvent } from 'react'
import {
  Alert,
  AppBar,
  Box,
  Button,
  Chip,
  CircularProgress,
  Collapse,
  Container,
  IconButton,
  LinearProgress,
  MenuItem,
  Paper,
  Stack,
  TextField,
  Toolbar,
  ToggleButton,
  ToggleButtonGroup,
  Typography,
} from '@mui/material'
import { checkSession, fetchCategories, logout, publishArticle } from './api'
import type { Category, PublishResult } from './api'
import { clearDraft, loadDraft, saveDraft } from './draftStore'
import LoginPage from './LoginPage'

interface ImageEntry {
  file: File
  previewUrl: string
  /** Photo credit, e.g. "צילום: ישראל ישראלי". Blank means no caption is added. */
  caption: string
}

const MIN_RAW_TEXT_LENGTH = 50
/** Must match ArticlePublisher.MaxRawTextLength on the server. */
const MAX_RAW_TEXT_LENGTH = 6000
/** Yoast's recommended minimum for an article body. Below it the form warns, but does not block. */
const MIN_WORD_COUNT = 300

function countWords(text: string): number {
  const trimmed = text.trim()
  return trimmed === '' ? 0 : trimmed.split(/\s+/).length
}

/** A small uppercase label with an accent bar, used to separate the form into scannable steps. */
function SectionLabel({ children }: { children: React.ReactNode }) {
  return (
    <Typography
      variant="overline"
      sx={{
        display: 'block',
        fontWeight: 700,
        color: 'primary.main',
        letterSpacing: 0.5,
        borderInlineStart: '3px solid',
        borderColor: 'primary.main',
        pl: 1.25,
        mb: 1,
      }}
    >
      {children}
    </Typography>
  )
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
  const [dragOver, setDragOver] = useState(false)
  const [submitting, setSubmitting] = useState<'publish' | 'draft' | null>(null)
  const [result, setResult] = useState<PublishResult | null>(null)
  // Saving starts only after the stored draft has been read, so the empty initial
  // state can never overwrite it.
  const [draftLoaded, setDraftLoaded] = useState(false)

  useEffect(() => {
    checkSession().then(setSession).catch(() => setSession(null))
  }, [])

  useEffect(() => {
    if (session) {
      fetchCategories().then(setCategories).catch(() => setCategories([]))
    }
  }, [session])

  useEffect(() => {
    loadDraft().then((draft) => {
      if (draft) {
        setRawText(draft.rawText)
        setUseAi(draft.useAi)
        setTitle(draft.title)
        setSubtitle(draft.subtitle)
        setCategoryId(draft.categoryId)
        setImages(
          draft.images.map((image) => ({
            file: image.file,
            previewUrl: URL.createObjectURL(image.file),
            caption: image.caption,
          })),
        )
        setFeaturedIndex(draft.featuredIndex)
      }
      setDraftLoaded(true)
    })
  }, [])

  useEffect(() => {
    if (!draftLoaded) return
    // Debounced so typing doesn't rewrite the stored images on every keystroke.
    const timer = window.setTimeout(() => {
      saveDraft({
        rawText,
        useAi,
        title,
        subtitle,
        categoryId,
        images: images.map(({ file, caption }) => ({ file, caption })),
        featuredIndex,
      })
    }, 400)
    return () => window.clearTimeout(timer)
  }, [draftLoaded, rawText, useAi, title, subtitle, categoryId, images, featuredIndex])

  useEffect(() => {
    // Release object URLs on unmount only; per-change cleanup happens in removeImage.
    return () => images.forEach((image) => URL.revokeObjectURL(image.previewUrl))
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  function addImages(fileList: FileList | null) {
    if (!fileList) return
    const added = Array.from(fileList)
      .filter((file) => file.type.startsWith('image/'))
      .map((file) => ({
        file,
        previewUrl: URL.createObjectURL(file),
        caption: '',
      }))
    setImages((prev) => [...prev, ...added])
  }

  function handleDrop(e: DragEvent<HTMLLabelElement>) {
    e.preventDefault()
    setDragOver(false)
    addImages(e.dataTransfer.files)
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

  const wordCount = countWords(rawText)

  const canSubmit =
    rawText.trim().length >= MIN_RAW_TEXT_LENGTH &&
    rawText.length <= MAX_RAW_TEXT_LENGTH &&
    categoryId !== '' &&
    images.length > 0 &&
    submitting === null &&
    (useAi || (title.trim().length > 0 && subtitle.trim().length > 0))

  function resetForm() {
    images.forEach((image) => URL.revokeObjectURL(image.previewUrl))
    setRawText('')
    setUseAi(true)
    setTitle('')
    setSubtitle('')
    setCategoryId('')
    setImages([])
    setFeaturedIndex(0)
  }

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
      // The article now exists on WordPress, so the saved draft has done its job.
      // A failed submit keeps everything so the operator can retry.
      if (outcome.success) {
        resetForm()
        clearDraft()
      }
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
    <Box sx={{ minHeight: '100dvh', bgcolor: 'background.default', pb: { xs: 10, sm: 4 } }}>
      <AppBar
        position="sticky"
        elevation={0}
        sx={{
          background: 'linear-gradient(135deg, #1a56db 0%, #1741a6 100%)',
        }}
      >
        <Toolbar sx={{ gap: 1, flexWrap: 'wrap', py: 1 }}>
          <Typography variant="h6" component="h1" sx={{ fontWeight: 700, flexGrow: 1 }}>
            פרסום כתבה
          </Typography>
          <Chip
            size="small"
            label={session.username}
            sx={{ bgcolor: 'rgba(255,255,255,0.15)', color: 'common.white', fontWeight: 600 }}
          />
          <Button size="small" onClick={handleLogout} sx={{ color: 'common.white' }}>
            התנתקות
          </Button>
        </Toolbar>
      </AppBar>

      <Container maxWidth="md" sx={{ pt: { xs: 2, sm: 3 } }}>
        <Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>
          הדביקו טקסט גולמי, בחרו קטגוריה, העלו תמונות וסמנו תמונה ראשית.
        </Typography>

        <Paper
          elevation={0}
          sx={{
            p: { xs: 2, sm: 3 },
            border: '1px solid',
            borderColor: 'divider',
            boxShadow: '0 8px 24px rgba(20, 40, 90, 0.06)',
          }}
        >
          <Stack spacing={3.5}>
            <Box>
              <SectionLabel>טקסט הכתבה</SectionLabel>
              <TextField
                placeholder="הדביקו כאן את הטקסט הגולמי של הכתבה..."
                multiline
                minRows={8}
                value={rawText}
                onChange={(e) => setRawText(e.target.value)}
                fullWidth
                // Hard stop: typing and pasting both get cut at the limit, so the text can never go over.
                slotProps={{ htmlInput: { maxLength: MAX_RAW_TEXT_LENGTH } }}
              />
              <Stack direction="row" sx={{ mt: 0.5, justifyContent: 'space-between' }}>
                <Typography variant="caption" color="text.secondary">
                  {rawText.trim().length < MIN_RAW_TEXT_LENGTH
                    ? `לפחות ${MIN_RAW_TEXT_LENGTH} תווים`
                    : rawText.length >= MAX_RAW_TEXT_LENGTH
                      ? 'הגעתם למגבלת התווים, הטקסט לא יכול להיות ארוך מזה'
                      : ''}
                </Typography>
                <Typography
                  variant="caption"
                  color={rawText.length >= MAX_RAW_TEXT_LENGTH ? 'error.main' : 'text.secondary'}
                >
                  {rawText.length} / {MAX_RAW_TEXT_LENGTH} תווים
                </Typography>
              </Stack>
              <Typography
                variant="caption"
                color={wordCount < MIN_WORD_COUNT ? 'warning.main' : 'text.secondary'}
                sx={{ display: 'block', mt: 0.5 }}
              >
                {wordCount} מילים
                {wordCount > 0 &&
                  wordCount < MIN_WORD_COUNT &&
                  ` - מתחת ל-${MIN_WORD_COUNT} מילים. הכתבה עלולה לקבל ציון SEO נמוך, מומלץ להוסיף תוכן.`}
              </Typography>
            </Box>

            <Box>
              <SectionLabel>עיבוד הכתבה</SectionLabel>
              <ToggleButtonGroup
                value={useAi ? 'ai' : 'manual'}
                exclusive
                fullWidth
                onChange={(_, value) => {
                  if (value) setUseAi(value === 'ai')
                }}
              >
                <ToggleButton value="ai" sx={{ py: 1.25 }}>
                  ✨ עם AI
                </ToggleButton>
                <ToggleButton value="manual" sx={{ py: 1.25 }}>
                  ✍️ בלי AI
                </ToggleButton>
              </ToggleButtonGroup>
              <Typography variant="caption" color="text.secondary" sx={{ display: 'block', mt: 0.5 }}>
                {useAi
                  ? 'ה-AI ינסח מחדש, ייצור כותרת ויחלק לפסקאות אוטומטית.'
                  : 'הטקסט יפורסם כפי שהוא, מחולק לפסקאות. הכותרות שלכם.'}
              </Typography>
            </Box>

            <Collapse in={!useAi} unmountOnExit>
              <Stack spacing={2}>
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
              </Stack>
            </Collapse>

            <Box>
              <SectionLabel>קטגוריה</SectionLabel>
              <TextField
                select
                value={categoryId}
                onChange={(e) => setCategoryId(Number(e.target.value))}
                fullWidth
                slotProps={{ select: { displayEmpty: true } }}
              >
                <MenuItem value="" disabled>
                  <Typography component="span" color="text.secondary">
                    בחרו קטגוריה
                  </Typography>
                </MenuItem>
                {categories.map((c) => (
                  <MenuItem key={c.id} value={c.id}>
                    {c.name}
                  </MenuItem>
                ))}
              </TextField>
            </Box>

            <Box>
              <SectionLabel>תמונות</SectionLabel>

              <Box
                component="label"
                onDragOver={(e) => {
                  e.preventDefault()
                  setDragOver(true)
                }}
                onDragLeave={() => setDragOver(false)}
                onDrop={handleDrop}
                sx={{
                  display: 'flex',
                  flexDirection: 'column',
                  alignItems: 'center',
                  justifyContent: 'center',
                  gap: 0.5,
                  textAlign: 'center',
                  cursor: 'pointer',
                  borderRadius: 3,
                  border: '2px dashed',
                  borderColor: dragOver ? 'primary.main' : 'divider',
                  bgcolor: dragOver ? 'action.hover' : 'transparent',
                  py: 3,
                  px: 2,
                  transition: 'all 0.15s ease',
                }}
              >
                <Typography variant="h5" component="span">
                  📷
                </Typography>
                <Typography variant="body2" sx={{ fontWeight: 600 }}>
                  לחצו להעלאת תמונות או גררו אותן לכאן
                </Typography>
                <Typography variant="caption" color="text.secondary">
                  ניתן לבחור כמה תמונות יחד
                </Typography>
                <input
                  type="file"
                  accept="image/*"
                  multiple
                  hidden
                  onChange={(e) => addImages(e.target.files)}
                />
              </Box>

              {images.length > 0 && (
                <Box
                  sx={{
                    mt: 2,
                    display: 'grid',
                    gridTemplateColumns: 'repeat(auto-fill, minmax(150px, 1fr))',
                    gap: 1.5,
                  }}
                >
                  {images.map((image, index) => {
                    const isFeatured = featuredIndex === index
                    return (
                      <Paper
                        key={image.previewUrl}
                        variant="outlined"
                        sx={{
                          overflow: 'hidden',
                          borderRadius: 2.5,
                          borderColor: isFeatured ? 'primary.main' : 'divider',
                          borderWidth: isFeatured ? 2 : 1,
                        }}
                      >
                        <Box sx={{ position: 'relative' }}>
                          <Box
                            component="img"
                            src={image.previewUrl}
                            alt=""
                            onClick={() => setFeaturedIndex(index)}
                            sx={{
                              width: '100%',
                              height: 110,
                              objectFit: 'cover',
                              display: 'block',
                              cursor: 'pointer',
                            }}
                          />
                          <Chip
                            size="small"
                            label="ראשית"
                            onClick={() => setFeaturedIndex(index)}
                            color={isFeatured ? 'primary' : 'default'}
                            sx={{
                              position: 'absolute',
                              insetInlineStart: 6,
                              bottom: 6,
                              fontWeight: 600,
                              cursor: 'pointer',
                              opacity: isFeatured ? 1 : 0.85,
                            }}
                          />
                          <IconButton
                            size="small"
                            onClick={() => removeImage(index)}
                            aria-label="הסרת תמונה"
                            sx={{
                              position: 'absolute',
                              insetInlineEnd: 4,
                              top: 4,
                              bgcolor: 'rgba(0,0,0,0.55)',
                              color: 'common.white',
                              '&:hover': { bgcolor: 'rgba(0,0,0,0.75)' },
                              width: 26,
                              height: 26,
                            }}
                          >
                            ✕
                          </IconButton>
                        </Box>
                        <TextField
                          size="small"
                          fullWidth
                          placeholder="קרדיט לתמונה (אופציונלי)"
                          value={image.caption}
                          onChange={(e) => updateCaption(index, e.target.value)}
                          variant="standard"
                          sx={{ px: 1, py: 0.5 }}
                        />
                      </Paper>
                    )
                  })}
                </Box>
              )}
            </Box>
          </Stack>
        </Paper>

        {result && (
          <Alert severity={result.success ? 'success' : 'error'} sx={{ mt: 3, borderRadius: 2.5 }}>
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

        {/* Desktop/tablet actions live inline; the sticky bar below takes over on mobile. */}
        <Stack
          direction="row"
          spacing={2}
          sx={{ mt: 3, display: { xs: 'none', sm: 'flex' } }}
        >
          <Button
            variant="outlined"
            size="large"
            disabled={!canSubmit}
            onClick={() => handleSubmit('draft')}
          >
            {submitting === 'draft' ? 'שומר טיוטה...' : 'שמירת טיוטה'}
          </Button>
          <Button
            variant="contained"
            size="large"
            disabled={!canSubmit}
            onClick={() => handleSubmit('publish')}
          >
            {submitting === 'publish' ? 'מפרסם...' : 'פרסום'}
          </Button>
        </Stack>
      </Container>

      {/* Mobile: actions pinned to the bottom of the viewport so they're always one thumb-reach away, no scrolling to find them. */}
      <Box
        sx={{
          display: { xs: 'flex', sm: 'none' },
          position: 'fixed',
          insetInline: 0,
          bottom: 0,
          gap: 1.5,
          p: 1.5,
          pb: 'calc(12px + env(safe-area-inset-bottom))',
          bgcolor: 'background.paper',
          borderTop: '1px solid',
          borderColor: 'divider',
          boxShadow: '0 -6px 20px rgba(20, 40, 90, 0.08)',
          zIndex: (t) => t.zIndex.appBar,
        }}
      >
        <Button
          variant="outlined"
          fullWidth
          size="large"
          disabled={!canSubmit}
          onClick={() => handleSubmit('draft')}
        >
          {submitting === 'draft' ? 'שומר...' : 'טיוטה'}
        </Button>
        <Button
          variant="contained"
          fullWidth
          size="large"
          disabled={!canSubmit}
          onClick={() => handleSubmit('publish')}
        >
          {submitting === 'publish' ? 'מפרסם...' : 'פרסום'}
        </Button>
      </Box>

      {submitting && (
        <LinearProgress
          sx={{ position: 'fixed', insetInline: 0, top: 0, zIndex: (t) => t.zIndex.tooltip }}
        />
      )}
    </Box>
  )
}

export default App
