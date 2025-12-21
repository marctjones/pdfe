# PdfEditor UX Redesign Proposal

**Version:** 1.0
**Date:** December 21, 2025
**Target:** Cross-platform PDF Editor with TRUE content-level redaction

---

## Executive Summary

This document proposes a comprehensive UX redesign for PdfEditor, focusing on:
1. **Clarity** - Making the redaction workflow intuitive from first use
2. **Efficiency** - Enabling quick mark-then-apply batch redaction workflows
3. **Cross-platform consistency** - A platform-agnostic design that feels native everywhere
4. **Progressive disclosure** - Simple on first use, powerful when needed

### Design Principles Applied

| Platform | Key Principles Adopted |
|----------|----------------------|
| **Windows 11 Fluent** | Rounded corners, material effects (Mica-inspired), comfortable spacing |
| **GNOME HIG** | "Do one thing well", reduce user effort, prevent errors |
| **macOS HIG** | Sidebar/content/inspector layout, toolbar organization, spatial consistency |
| **Avalonia Best Practices** | MVVM patterns, reactive UI, SplitView for sidebars |

---

## Current State Analysis

### Current UI Structure
```
+------------------------------------------------------------------+
| Menu Bar (File, Edit, Document, Redaction, Tools, View, Help)    |
+------------------------------------------------------------------+
| Title Bar (PDF Editor | Document Name)                           |
+------------------------------------------------------------------+
| Toolbar (Open, Save | Select Text, Redact, Apply | Find, OCR...) |
+------------------------------------------------------------------+
| Search Bar (conditional)                                          |
+------------------------------------------------------------------+
| Thumbnails  |        PDF Viewer          | Clipboard History     |
| Sidebar     |                            | Sidebar               |
| (200px)     |                            | (250px)               |
+------------------------------------------------------------------+
| Status Bar (Mode | Status | Page Navigation)                     |
+------------------------------------------------------------------+
```

### Identified Pain Points

1. **Mode Switching Confusion**
   - Users must click "Redact" button to enter redaction mode
   - No clear visual indicator that they're in a special mode
   - "Apply" button only appears after entering mode
   - Users expect to just select and redact without mode switching

2. **Workflow Fragmentation**
   - Current flow: Enter Mode -> Select -> Apply (per selection)
   - Users want: Select multiple areas -> Review -> Apply all at once

3. **Sidebar Clutter**
   - Clipboard History shows past redactions but not pending ones
   - Thumbnails always visible even when not needed
   - No way to collapse sidebars for focused document viewing

4. **Visual Feedback Gaps**
   - Pending vs applied redactions look similar
   - No visual inventory of what will be redacted

---

## Proposed Design

### Layout Overview

```
+------------------------------------------------------------------+
| Menu Bar                                                          |
+------------------------------------------------------------------+
|                     Unified Toolbar                               |
| [Open][Save][|][Pan][Select][Redact][|][Find][OCR][|][-][100%][+]|
+------------------------------------------------------------------+
| Thumb- |                                      | Redaction        |
| nails  |                                      | Panel            |
| Panel  |        Document Canvas               | (context-        |
|        |                                      |  sensitive)      |
| [<<]   |     [PDF content with overlays]     |                  |
|        |                                      | - Pending (3)    |
|        |                                      | - Applied (5)    |
|        |                                      |                  |
|        |                                      | [Review] [Apply] |
+------------------------------------------------------------------+
| [View Mode] | Status Message           | < Page 3 of 12 >       |
+------------------------------------------------------------------+
```

### Key Design Changes

#### 1. Unified Single Toolbar

Replace the current dual row (title bar + toolbar) with a single, well-organized toolbar.

```
+------------------------------------------------------------------+
|  Toolbar Groups (Left to Right):                                  |
|                                                                   |
|  [FILE]     [MODE]           [TOOLS]        [VIEW]               |
|  Open       Pan (default)    Find           Zoom -               |
|  Save       Select Text      OCR            100%                 |
|             Redact                          Zoom +               |
|                                             Fit                  |
+------------------------------------------------------------------+
```

**Toolbar Button Specifications:**

| Group | Button | Icon | Behavior | Keyboard |
|-------|--------|------|----------|----------|
| File | Open | Folder icon | Opens file dialog | Ctrl+O |
| File | Save | Disk icon | Save (disabled until changes) | Ctrl+S |
| Mode | Pan | Hand icon | Default mode, drag to scroll | P |
| Mode | Select | Text cursor | Select & copy text | T |
| Mode | Redact | Black rectangle | Enter redaction marking mode | R |
| Tools | Find | Magnifier | Toggle search bar | Ctrl+F |
| Tools | OCR | Document scan icon | Run OCR on current page | - |
| View | Zoom Out | Minus | Decrease zoom | Ctrl+- |
| View | Zoom % | Text display | Shows current zoom level | - |
| View | Zoom In | Plus | Increase zoom | Ctrl++ |
| View | Fit | Expand arrows | Fit width toggle | Ctrl+1 |

**Mode Toggle Behavior:**
- Mode buttons are mutually exclusive toggle buttons
- Selected mode has distinct visual treatment (filled background, accent border)
- Pan is the default mode when opening a document

#### 2. Context-Sensitive Right Panel

The right sidebar transforms based on current activity:

**State A: No Document Open**
```
+-------------------+
|                   |
|   [Drag PDF here] |
|        or         |
|   [Open File]     |
|                   |
+-------------------+
```

**State B: Viewing/Pan Mode**
```
+-------------------+
| Document Info     |
+-------------------+
| Pages: 12         |
| Size: 2.4 MB      |
| Modified: Today   |
+-------------------+
|                   |
| [Recent Actions]  |
| - Opened file     |
| - Page 3 viewed   |
|                   |
+-------------------+
```

**State C: Redaction Mode (Key Innovation)**
```
+-------------------+
| REDACTION         |
| -----------       |
| Mark areas to     |
| redact, then      |
| review and apply. |
+-------------------+
| PENDING (3)       |
+-------------------+
| [x] Page 1        |
|     "John Smith"  |
|     128x24 px     |
+-------------------+
| [x] Page 1        |
|     "555-1234"    |
|     96x18 px      |
+-------------------+
| [x] Page 3        |
|     [image]       |
|     200x150 px    |
+-------------------+
|                   |
| [Review All]      |
| [Apply All (3)]   |
|                   |
+-------------------+
| APPLIED (5)       |
+-------------------+
| Page 1 (2 items)  |
| Page 2 (3 items)  |
+-------------------+
```

**Key Features:**
- Checkboxes allow selecting which pending redactions to apply
- "Review All" opens confirmation dialog with preview
- "Apply All" applies only checked items
- Expandable sections show applied redaction history
- Click on pending item scrolls to and highlights that area

#### 3. Improved Mode Indication

**Current Problem:** Mode state is only shown in small status bar text.

**Solution:** Multi-layered mode feedback

**A. Toolbar Toggle States**
```
INACTIVE:                    ACTIVE:
+--------+                   +--------+
| Redact |                   | Redact |  <- Filled background
+--------+                   +--------+     Accent border
                                           Bold text
```

**B. Document Canvas Border**
When in Redaction mode, add a subtle colored border around the document area:
```
+------------------------------------------------------------------+
|                                                                   |
|  +---[Document Canvas]------------------------------------------+ |
|  |                            2px accent border when in         | |
|  |                            redaction mode                    | |
|  |                                                              | |
|  +--------------------------------------------------------------+ |
|                                                                   |
+------------------------------------------------------------------+
```

**C. Cursor Changes**
| Mode | Cursor |
|------|--------|
| Pan | Hand (grab) |
| Select Text | I-beam |
| Redact | Crosshair |

**D. Status Bar Mode Chip**
```
+------------------------------------------------------------------+
| [REDACT MODE] | 3 areas marked           | < Page 3 of 12 >     |
+------------------------------------------------------------------+
     ^
     Colored chip: red for redact, blue for select, gray for pan
```

#### 4. Redaction Visual Language

**Pending Redaction (Not Yet Applied):**
```
+---------------------------+
|  REDACTED TEXT HERE       |  <- Red dashed border (2px)
|                           |     Semi-transparent red fill (15%)
+---------------------------+     Small "1" badge in corner
```

**Confirmed/Applied Redaction:**
```
+---------------------------+
|                           |  <- Solid black fill
|   [REDACTED]              |     White "REDACTED" text (optional)
|                           |
+---------------------------+
```

**Hover State (Pending):**
```
+---------------------------+
|  REDACTED TEXT HERE       |  <- Highlight effect
|                    [x][~] |     Quick actions appear:
+---------------------------+     [x] Remove, [~] Adjust
```

#### 5. Collapsible Sidebars

Both sidebars become collapsible with consistent controls:

**Expanded State:**
```
+--------+                              +-------------------+
| Pages  |  [<<]                        |        [>>]       |
+--------+                              +-------------------+
| [Thumb |                              | Redaction Panel   |
|  nail  |                              | content...        |
|  list] |                              |                   |
+--------+                              +-------------------+
```

**Collapsed State:**
```
+--+                                                      +--+
|>>|                                                      |<<|
+--+                                                      +--+
|P |  <- Vertical text "Pages"                           |R |
|a |                                                      |e |
|g |                                                      |d |
|e |                                                      |a |
|s |                                                      |c |
+--+                                                      |t |
                                                          +--+
```

**Sidebar Toggle Button in Toolbar:**
Add a toggle icon at each edge of the toolbar:
```
[|<] Open | Save | ... | Find | OCR | ... | Zoom | [>|]
 ^                                                  ^
 Toggle left sidebar                    Toggle right sidebar
```

#### 6. Search Bar Integration

Move search from conditional row to integrated overlay:

```
+------------------------------------------------------------------+
| Toolbar...                                                        |
+------------------------------------------------------------------+
|        +--------------------------------------------------+       |
|        | [x] Find: [search text here____] [<] [>] 3 of 12|       |
|        | [ ] Case  [ ] Whole words                       |       |
|        +--------------------------------------------------+       |
|                                                                   |
|        [Document Canvas]                                          |
|                                                                   |
```

- Search bar overlays document area (not a separate row)
- Closes with Escape or X button
- Results count always visible
- Previous/Next navigation with keyboard shortcuts

#### 7. First-Use Guidance

**Approach:** Subtle, dismissible inline hints that appear once.

**Scenario: User opens first PDF**
```
+------------------------------------------------------------------+
|                                                                   |
|  +--------------------------------------------------------------+ |
|  |                                                              | |
|  |     [Document content]                                       | |
|  |                                                              | |
|  +--------------------------------------------------------------+ |
|                                                                   |
|  +--[Floating tip]------------------------------------------+     |
|  | TIP: Press R or click Redact to start marking sensitive  |     |
|  |      content for removal.                     [Got it]   |     |
|  +----------------------------------------------------------+     |
+------------------------------------------------------------------+
```

**Scenario: User enters Redaction mode for first time**
```
+-------------------+
| REDACTION         |
+-------------------+
| Drag to mark      |
| areas to redact.  |
|                   |
| Click "Apply All" |
| when ready.       |
|                   |
| [Don't show again]|
+-------------------+
```

**Rules for tips:**
- Show each tip only once per user (persist preference)
- Tips are contextual (only show when relevant)
- Always dismissible with clear "Got it" or "Don't show again"
- Never interrupt workflow - appear in sidebar or as overlay

---

## Detailed Workflows

### Workflow 1: Basic Redaction (Primary Use Case)

**User Goal:** Read a PDF and redact sensitive information found while reading.

```
STEP 1: Open Document
+------------------+
| User clicks Open |
| or drags PDF     |
+--------+---------+
         |
         v
+------------------+
| PDF loads        |
| Pan mode active  |
| (default)        |
+--------+---------+
         |
STEP 2: Navigate and Find Sensitive Content
         |
         v
+------------------+
| User scrolls/    |
| reads document   |
| Finds sensitive  |
| text on Page 3   |
+--------+---------+
         |
STEP 3: Enter Redaction Mode
         |
         v
+------------------+
| User presses R   |
| or clicks Redact |
|                  |
| Visual feedback: |
| - Toolbar toggle |
| - Border color   |
| - Cursor change  |
| - Right panel    |
|   shows redact UI|
+--------+---------+
         |
STEP 4: Mark Areas
         |
         v
+------------------+
| User drags to    |
| select area      |
|                  |
| Area appears     |
| with red dashed  |
| border           |
|                  |
| Added to pending |
| list in sidebar  |
+--------+---------+
         |
         | (Repeat for more areas)
         v
STEP 5: Review and Apply
         |
         v
+------------------+
| User clicks      |
| "Apply All" or   |
| "Review All"     |
|                  |
| If Review:       |
| Dialog shows     |
| preview of all   |
| redactions       |
+--------+---------+
         |
         v
+------------------+
| Redactions       |
| applied          |
| Content removed  |
| Black boxes      |
| replace content  |
+--------+---------+
         |
STEP 6: Save
         |
         v
+------------------+
| User saves       |
| (Ctrl+S)         |
| Document now     |
| contains true    |
| redactions       |
+------------------+
```

### Workflow 2: Quick Single Redaction

For users who want immediate redaction without batching:

```
Option A: Enter Redact mode, hold Shift while selecting
          -> Immediately applies on release

Option B: Right-click selection -> "Redact Now"
          -> Skips pending queue

Option C: Keyboard: Select area, press Enter
          -> Applies just that selection
```

### Workflow 3: Adjusting Pending Redactions

```
SCENARIO: User marked wrong area

METHOD 1: Click X on overlay
+---------------------------+
|  TEXT TO KEEP        [x]  |  <- Click X to remove
+---------------------------+

METHOD 2: Uncheck in sidebar
+-------------------+
| PENDING (3)       |
+-------------------+
| [ ] Page 1        |  <- Uncheck to exclude
|     "John Smith"  |
+-------------------+

METHOD 3: Click to resize
+---------------------------+
|  TEXT TO REDACT     [~]   |  <- Click ~ to enter resize mode
+---------------------------+
         |
         v
+------+-------------------+------+
|  o   |  Drag handles     |  o   |  <- Resize handles appear
+------+-------------------+------+
```

---

## Information Architecture

### Menu Structure (Revised)

```
File
  Open...                    Ctrl+O
  Open Recent               >
  ---
  Save                       Ctrl+S
  Save As...                 Ctrl+Shift+S
  ---
  Export Pages as Images...
  Print...                   Ctrl+P
  ---
  Close Document             Ctrl+W
  Exit                       Alt+F4

Edit
  Undo                       Ctrl+Z
  ---
  Find...                    Ctrl+F
  Find Next                  F3
  Find Previous              Shift+F3
  ---
  Copy                       Ctrl+C
  ---
  Preferences...             Ctrl+,

View
  Zoom In                    Ctrl+=
  Zoom Out                   Ctrl+-
  Actual Size                Ctrl+0
  Fit Width                  Ctrl+1
  Fit Page                   Ctrl+2
  ---
  Show Thumbnails            Ctrl+T
  Show Redaction Panel       Ctrl+Shift+R
  ---
  Theme                     >
    Light
    Dark
    System Default

Document
  Add Pages...
  Remove Current Page
  ---
  Rotate Left                Ctrl+L
  Rotate Right               Ctrl+R
  Rotate 180
  ---
  Go to Page...              Ctrl+G
  First Page                 Home
  Last Page                  End

Redaction
  Redaction Mode             R
  ---
  Apply Selected Redactions  Enter
  Apply All Redactions       Ctrl+Enter
  Clear All Pending          Ctrl+Shift+Delete
  ---
  Verify Redaction
  ---
  Redaction History...

Tools
  OCR Current Page...
  OCR All Pages...
  ---
  Verify Signatures...

Help
  Keyboard Shortcuts         F1
  Documentation...
  ---
  About PDF Editor
```

### Keyboard Shortcuts (Complete)

| Category | Action | Shortcut |
|----------|--------|----------|
| **File** | Open | Ctrl+O |
| | Save | Ctrl+S |
| | Save As | Ctrl+Shift+S |
| | Close | Ctrl+W |
| | Print | Ctrl+P |
| **Navigation** | Next Page | Page Down / Right |
| | Previous Page | Page Up / Left |
| | First Page | Home |
| | Last Page | End |
| | Go to Page | Ctrl+G |
| **Zoom** | Zoom In | Ctrl+= or Ctrl++ |
| | Zoom Out | Ctrl+- |
| | Actual Size | Ctrl+0 |
| | Fit Width | Ctrl+1 |
| | Fit Page | Ctrl+2 |
| **Mode** | Pan Mode | P |
| | Text Select Mode | T |
| | Redaction Mode | R |
| **Edit** | Find | Ctrl+F |
| | Find Next | F3 |
| | Find Previous | Shift+F3 |
| | Copy | Ctrl+C |
| | Undo | Ctrl+Z |
| **Redaction** | Apply Selection | Enter |
| | Apply All | Ctrl+Enter |
| | Cancel/Remove Last | Escape |
| | Quick Redact (in mode) | Shift+Drag |
| **View** | Toggle Thumbnails | Ctrl+T |
| | Toggle Redaction Panel | Ctrl+Shift+R |
| **Help** | Shortcuts | F1 |

---

## Visual Design Specifications

### Color Palette

**Light Theme:**
```
Background (Window):     #FAFAFA
Background (Sidebar):    #F5F5F5
Background (Card):       #FFFFFF
Background (Document):   #E0E0E0
Border (Subtle):         #E0E0E0
Border (Standard):       #BDBDBD
Text (Primary):          #212121
Text (Secondary):        #757575
Text (Tertiary):         #9E9E9E
Accent (Primary):        #0078D4 (Blue)
Accent (Danger):         #D32F2F (Red - for redaction)
Accent (Success):        #388E3C (Green)
```

**Dark Theme:**
```
Background (Window):     #1E1E1E
Background (Sidebar):    #252526
Background (Card):       #2D2D30
Background (Document):   #1A1A1A
Border (Subtle):         #3C3C3C
Border (Standard):       #4D4D4D
Text (Primary):          #FFFFFF
Text (Secondary):        #CCCCCC
Text (Tertiary):         #808080
Accent (Primary):        #3794FF
Accent (Danger):         #F44336
Accent (Success):        #4CAF50
```

### Typography

**Font Family:** System font stack
- Windows: Segoe UI Variable
- macOS: SF Pro
- Linux: Cantarell, Ubuntu, or system default

**Scale:**
```
Title:      16px / 600 weight
Heading:    14px / 600 weight
Body:       13px / 400 weight
Caption:    11px / 400 weight
Label:      12px / 500 weight
```

### Spacing System

Use 4px base unit:
```
xs:  4px   (between related elements)
sm:  8px   (standard padding)
md:  12px  (card padding)
lg:  16px  (section spacing)
xl:  24px  (major sections)
```

### Corner Radius

```
Small (buttons, inputs):   4px
Medium (cards, panels):    6px
Large (dialogs):           8px
```

### Shadows

**Light theme only** (dark theme uses borders):
```
Card:    0 1px 3px rgba(0,0,0,0.08)
Popup:   0 4px 12px rgba(0,0,0,0.15)
Dialog:  0 8px 24px rgba(0,0,0,0.20)
```

---

## Component Specifications

### Toolbar Button

```
+------------------+
|                  |
|   [Icon] Label   |  Height: 32px
|                  |  Padding: 12px horizontal, 6px vertical
+------------------+  Corner radius: 4px
                      Font: 13px

States:
- Default:    Transparent background, border on hover
- Hover:      Light fill (#F0F0F0 light, #3C3C3C dark)
- Pressed:    Darker fill (#E0E0E0 light, #4D4D4D dark)
- Active:     Accent background, white text
- Disabled:   50% opacity
```

### Mode Toggle Button

```
INACTIVE:
+------------------+
|   [Icon] Redact  |  Transparent background
+------------------+  Standard text color

ACTIVE:
+------------------+
| * [Icon] Redact  |  Accent color background
+------------------+  White text
                      2px accent border
                      Subtle glow/shadow
```

### Sidebar Panel

```
+----------------------+
| HEADER          [>>] |  Header: 40px height
+----------------------+  Background: slightly darker
|                      |  Toggle button at right
|   Content area       |
|                      |  Content: scrollable
|                      |  Padding: 8px
|                      |
+----------------------+
```

### Pending Redaction Card

```
+------------------------+
| [x] Page 1        [X]  |  Checkbox + page number + remove
+------------------------+
| "Sensitive text..."    |  Preview text (truncated)
| 128 x 24 px            |  Dimensions
+------------------------+
                            Margin: 8px
                            Padding: 8px
                            Border: 1px subtle
                            Hover: highlight border
                            Click: navigate to area
```

### Apply Button (Primary Action)

```
+---------------------------+
|     APPLY ALL (3)         |  Full width of panel
+---------------------------+  Height: 40px
                               Background: Accent/Danger red
                               Text: White, 14px, bold
                               Corner radius: 6px

Disabled state:
+---------------------------+
|     APPLY ALL             |  Grayed out when no pending
+---------------------------+  No count shown
```

---

## Mockup: Main Window

### Light Theme - Redaction Mode Active

```
+====================================================================+
| File  Edit  View  Document  Redaction  Tools  Help                 |
+====================================================================+
|                                                                    |
| [|<] [Open] [Save] | [Pan] [Select] [*REDACT*] | [Find] [OCR] |   |
|      [-] 100% [+] [Fit]                                      [>|] |
+====================================================================+
|      |                                              |              |
| PAGE |  +----------------------------------------+  | REDACTION    |
| NAILS|  |                                        |  +--------------+
|      |  |    CONFIDENTIAL DOCUMENT               |  | Mark areas   |
|[P.1] |  |                                        |  | to redact.   |
|      |  |    Name: [John Smith]  <- RED DASHED   |  |              |
|[P.2] |  |    SSN: [###-##-####]  <- PENDING      |  +--------------+
|      |  |    Phone: 555-1234                     |  | PENDING (2)  |
|[P.3] |  |                                        |  +--------------+
| sel  |  |    Address:                            |  | [x] Page 1   |
|      |  |    123 Main Street                     |  |  "John Smith"|
|[P.4] |  |    City, ST 12345                      |  |  96x18       |
|      |  |                                        |  +--------------+
|      |  |                                        |  | [x] Page 1   |
|      |  +----------------------------------------+  |  "###-##-..." |
|      |              2px red border                  |  112x18      |
| [<<] |                                              +--------------+
|      |                                              |              |
|      |                                              | [Review All] |
|      |                                              | [APPLY (2)]  |
|      |                                              +--------------+
|      |                                              | APPLIED (0)  |
|      |                                              | (none yet)   |
+------+----------------------------------------------+--------------+
| [REDACT MODE]  | 2 areas marked               |  < Page 1 of 4 >  |
+====================================================================+
```

### Dark Theme - Normal Viewing

```
+====================================================================+
| File  Edit  View  Document  Redaction  Tools  Help        [DARK]   |
+====================================================================+
|                                                                    |
| [|<] [Open] [Save] | [*Pan*] [Select] [Redact] | [Find] [OCR] |   |
|      [-] 125% [+] [Fit]                                      [>|] |
+====================================================================+
|      |                                              |              |
| PAGE |  +----------------------------------------+  | DOCUMENT     |
| NAILS|  |                                        |  +--------------+
|      |  |    CONFIDENTIAL DOCUMENT               |  | Pages: 4     |
|[P.1] |  |                                        |  | Size: 1.2 MB |
|      |  |    Name: John Smith                    |  | Modified:    |
|[P.2] |  |    SSN: [REDACTED]  <- SOLID BLACK    |  |   Today      |
| sel  |  |    Phone: 555-1234                     |  |              |
|[P.3] |  |                                        |  +--------------+
|      |  |    Address:                            |  |              |
|[P.4] |  |    123 Main Street                     |  | TIP: Press R |
|      |  |    City, ST 12345                      |  | to start     |
|      |  |                                        |  | redacting    |
|      |  +----------------------------------------+  |              |
|      |                                              | [Got it]     |
| [<<] |                                              |              |
+------+----------------------------------------------+--------------+
| [PAN MODE]  | Document loaded                 |  < Page 2 of 4 >  |
+====================================================================+
```

### Collapsed Sidebars View

```
+====================================================================+
| File  Edit  View  Document  Redaction  Tools  Help                 |
+====================================================================+
|[|<] [Open] [Save] | [Pan] [Select] [Redact] | [Find] [-] 100% [+] |
+====================================================================+
|  |                                                              |  |
|P |  +--------------------------------------------------------+  |R |
|a |  |                                                        |  |e |
|g |  |                                                        |  |d |
|e |  |           MAXIMUM DOCUMENT VIEWING AREA                |  |a |
|s |  |                                                        |  |c |
|  |  |                                                        |  |t |
|>>|  |                                                        |  |<<|
|  |  +--------------------------------------------------------+  |  |
+--+--------------------------------------------------------------+--+
| [PAN MODE]  | Sidebars collapsed              |  < Page 1 of 4 >  |
+====================================================================+
```

---

## Confirmation Dialog: Review All Redactions

```
+================================================================+
|  Review Pending Redactions                              [X]    |
+================================================================+
|                                                                |
|  You are about to permanently remove content from 3 areas.     |
|  This action cannot be undone after saving.                    |
|                                                                |
|  +----------------------------------------------------------+  |
|  |  [x]  Page 1: "John Smith"                               |  |
|  |       Area: 128 x 24 pixels                              |  |
|  |       [Preview thumbnail of area]                        |  |
|  +----------------------------------------------------------+  |
|  |  [x]  Page 1: "555-123-4567"                             |  |
|  |       Area: 112 x 18 pixels                              |  |
|  |       [Preview thumbnail of area]                        |  |
|  +----------------------------------------------------------+  |
|  |  [x]  Page 3: [Image content]                            |  |
|  |       Area: 200 x 150 pixels                             |  |
|  |       [Preview thumbnail of area]                        |  |
|  +----------------------------------------------------------+  |
|                                                                |
|  WARNING: Redacted content will be PERMANENTLY REMOVED from    |
|  the PDF structure. Text extraction tools will not be able     |
|  to recover this content.                                      |
|                                                                |
|                              [Cancel]    [Apply 3 Redactions]  |
+================================================================+
```

---

## Accessibility Considerations

### Keyboard Navigation

1. **Full keyboard accessibility**
   - Tab order follows logical layout: Toolbar -> Left Sidebar -> Document -> Right Sidebar -> Status Bar
   - Arrow keys navigate within panels
   - Enter activates buttons and selects items
   - Escape closes dialogs and exits modes

2. **Focus indicators**
   - 2px accent-colored outline on focused elements
   - High contrast in both light and dark themes

3. **Skip links**
   - Hidden "Skip to document" link for screen readers

### Screen Reader Support

1. **ARIA labels** on all interactive elements
2. **Live regions** for status updates
3. **Announced mode changes** ("Entering redaction mode")
4. **Described redaction areas** ("Pending redaction 1 of 3, page 1, area 128 by 24 pixels")

### Visual Accessibility

1. **Color contrast** - All text meets WCAG AA (4.5:1 for normal text, 3:1 for large)
2. **Not color-only** - Status never conveyed by color alone (icons + text)
3. **Scalable UI** - Respects system font size preferences
4. **Reduced motion** - Animations can be disabled via system settings

---

## Cross-Platform Considerations

### Platform-Specific Adaptations

| Element | Windows | macOS | Linux |
|---------|---------|-------|-------|
| **Title Bar** | Extend into client area | Standard system | Standard system |
| **Menu Bar** | In-window | System menu bar (optional) | In-window |
| **Font** | Segoe UI Variable | SF Pro | System default |
| **Scrollbars** | Always visible | Overlay | System preference |
| **File Dialogs** | Native | Native | GTK/Qt |
| **Keyboard** | Ctrl+key | Cmd+key | Ctrl+key |

### Consistency Principles

1. **Behavior over appearance** - Same workflows on all platforms
2. **Native feel where it matters** - File dialogs, scrollbars, fonts
3. **Custom where consistent** - Toolbar, panels, document canvas
4. **Respect system settings** - Theme, font size, animations

---

## Implementation Priority

### Phase 1: Core Workflow Fix (High Priority)
1. Add "Pending Redactions" section to right panel
2. Implement batch apply with "Apply All" button
3. Add visual distinction between pending/applied redactions
4. Improve mode indication (toolbar toggle states, cursor changes)

### Phase 2: Navigation & Layout (Medium Priority)
5. Make sidebars collapsible
6. Add sidebar toggle buttons to toolbar
7. Implement search bar as overlay
8. Add dark theme support

### Phase 3: Polish & Onboarding (Lower Priority)
9. Add first-use tips (dismissible)
10. Implement Review All confirmation dialog
11. Add keyboard shortcut overlay (F1)
12. Performance optimization for large documents

---

## Success Metrics

1. **Task completion rate** - Users can complete redaction without help
2. **Error rate** - Fewer accidental redactions or missed content
3. **Time to first redaction** - Reduced time from open to first applied redaction
4. **Mode confusion incidents** - Eliminate "I thought I was redacting" issues

---

## Research Sources

This proposal was informed by the following design guidelines:

- [Windows 11 Design Principles](https://learn.microsoft.com/en-us/windows/apps/design/signature-experiences/design-principles) - Microsoft Learn
- [Fluent 2 Design System](https://fluent2.microsoft.design/components/windows) - Microsoft
- [GNOME Human Interface Guidelines](https://developer.gnome.org/hig/) - GNOME Developer
- [GNOME Design Principles](https://developer.gnome.org/hig/principles.html) - GNOME Developer
- [Apple Human Interface Guidelines](https://developer.apple.com/design/human-interface-guidelines/) - Apple Developer
- [macOS Toolbars](https://developers.apple.com/design/human-interface-guidelines/components/menus-and-actions/toolbars/) - Apple Developer
- [macOS Sidebars](https://developer.apple.com/design/human-interface-guidelines/sidebars) - Apple Developer
- [Avalonia UI Documentation](https://docs.avaloniaui.net/) - Avalonia
- [Avalonia MVVM Pattern](https://docs.avaloniaui.net/docs/concepts/the-mvvm-pattern/avalonia-ui-and-mvvm) - Avalonia Docs
- [Windows 11 Mica Material](https://learn.microsoft.com/en-us/windows/apps/design/style/mica) - Microsoft Learn
- [Windows Typography](https://learn.microsoft.com/en-us/windows/apps/design/signature-experiences/typography) - Microsoft Learn

---

## Appendix A: Current vs Proposed Comparison

| Aspect | Current | Proposed |
|--------|---------|----------|
| **Redaction Flow** | Enter mode -> Select -> Apply (each) | Mark multiple -> Review -> Apply all |
| **Mode Indication** | Status bar text only | Toolbar toggle + border + cursor + status chip |
| **Pending List** | None | Right sidebar with checkboxes |
| **Sidebars** | Fixed width, always visible | Collapsible, toggle buttons |
| **Search** | Separate toolbar row | Overlay on document area |
| **Theme** | Light only | Light + Dark + System |
| **First-use Help** | None | Contextual dismissible tips |
| **Batch Apply** | Not supported | "Apply All" + "Review All" |

---

*End of UX Redesign Proposal*
