import { useEffect, useMemo, useState } from 'react'
import { Badge } from '@/components/ui/badge'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Input } from '@/components/ui/input'
import { ScrollArea } from '@/components/ui/scroll-area'
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table'
import { Tabs, TabsContent, TabsList, TabsTrigger } from '@/components/ui/tabs'
import {
  categoryLabel,
  describeError,
  studioApi,
  type CatalogBrand,
  type CatalogOverview,
} from '@/lib/studio-api'
import { cn } from '@/lib/utils'
import { PageShell } from './page-shell'

export function CatalogPage() {
  const [catalog, setCatalog] = useState<CatalogOverview | null>(null)
  const [brandId, setBrandId] = useState('')
  const [query, setQuery] = useState('')
  const [message, setMessage] = useState('')

  useEffect(() => {
    void studioApi<CatalogOverview>('/api/v1/catalog/brands')
      .then((overview) => {
        setCatalog(overview)
        setBrandId((current) => current || overview.brands[0]?.id || '')
      })
      .catch((error: unknown) => setMessage(describeError(error)))
  }, [])

  const brand = catalog?.brands.find((item) => item.id === brandId)
  const needle = query.trim().toLowerCase()
  const canonical = useMemo(() => {
    const items = catalog?.items ?? []
    if (!needle) return items
    return items.filter((item) =>
      [item.id, item.nameZh, item.category, ...(item.sources ?? [])].join(' ').toLowerCase().includes(needle)
    )
  }, [catalog, needle])

  return (
    <PageShell
      title='品牌目录'
      description={
        catalog
          ? `目录版本 ${catalog.version}。共 ${catalog.brands.length} 个品牌、${catalog.items.length} 个标准数据项。品牌专有项只出现在对应品牌下。`
          : '正在读取数控品牌目录…'
      }
    >
      {message ? <p className='mb-3 text-sm text-destructive'>{message}</p> : null}
      <Tabs defaultValue='brands'>
        <TabsList>
          <TabsTrigger value='brands'>品牌与型号</TabsTrigger>
          <TabsTrigger value='items'>标准数据项</TabsTrigger>
        </TabsList>
        <TabsContent value='brands' className='mt-3'>
          <div className='grid items-start gap-3 lg:grid-cols-[16rem_minmax(0,1fr)]'>
            <Card className='gap-0 overflow-hidden py-0'>
              <div className='border-b px-3 py-2 text-sm font-medium'>品牌</div>
              <ScrollArea className='h-[min(36rem,calc(100vh-16rem))]'>
                <nav className='grid gap-0.5 p-1.5'>
                  {(catalog?.brands ?? []).map((item) => (
                    <button
                      key={item.id}
                      type='button'
                      className={cn(
                        'rounded-md px-2 py-2 text-start text-sm hover:bg-accent',
                        item.id === brandId && 'bg-accent font-medium'
                      )}
                      onClick={() => setBrandId(item.id)}
                    >
                      <span className='block'>{item.nameZh}</span>
                      <span className='block text-xs text-muted-foreground'>{item.nameEn}</span>
                    </button>
                  ))}
                </nav>
              </ScrollArea>
            </Card>
            {brand ? <BrandDetail brand={brand} /> : null}
          </div>
        </TabsContent>
        <TabsContent value='items' className='mt-3'>
          <Card>
            <CardHeader className='gap-3'>
              <CardTitle>标准数据项</CardTitle>
              <Input
                value={query}
                placeholder='搜索中文名、英文 Id 或分类'
                onChange={(event) => setQuery(event.target.value)}
              />
            </CardHeader>
            <CardContent>
              <ItemTable
                rows={canonical.map((item) => ({
                  id: item.id,
                  nameZh: item.nameZh,
                  dataType: item.dataType,
                  unit: item.unit,
                  category: item.category,
                  extra: (item.sources ?? []).join('、'),
                }))}
                extraLabel='来源名称'
              />
            </CardContent>
          </Card>
        </TabsContent>
      </Tabs>
    </PageShell>
  )
}

function BrandDetail({ brand }: { brand: CatalogBrand }) {
  return (
    <div className='grid min-w-0 gap-3'>
      <Card>
        <CardHeader>
          <CardTitle>
            {brand.nameZh}
            <span className='ms-2 text-sm font-normal text-muted-foreground'>{brand.nameEn}</span>
          </CardTitle>
        </CardHeader>
        <CardContent className='grid gap-3'>
          <div>
            <p className='mb-2 text-sm font-medium'>控制器型号</p>
            {brand.models.length === 0 ? (
              <p className='text-sm text-muted-foreground'>型号表未单列系列，创建设备时可以不填型号。</p>
            ) : (
              <div className='flex flex-wrap gap-2'>
                {brand.models.map((model) => (
                  <Badge key={model.id} variant='secondary'>
                    {model.name}
                  </Badge>
                ))}
              </div>
            )}
          </div>
          <div>
            <p className='mb-2 text-sm font-medium'>适配器</p>
            <div className='grid gap-2'>
              {brand.adapters.map((adapter) => (
                <div key={adapter.id} className='rounded-md border px-3 py-2 text-sm'>
                  <div className='font-medium'>
                    {adapter.displayName}
                    <span className='ms-2 font-mono text-xs text-muted-foreground'>{adapter.id}</span>
                  </div>
                  <div className='text-xs text-muted-foreground'>
                    {adapter.kind === 'simulator' ? '模拟器' : '协议驱动'} · {adapter.protocol}
                    {adapter.phase > 1 ? ' · 第二阶段' : ''}
                  </div>
                </div>
              ))}
            </div>
          </div>
        </CardContent>
      </Card>
      <Card>
        <CardHeader>
          <CardTitle>该品牌数据项（{brand.items.length}）</CardTitle>
        </CardHeader>
        <CardContent>
          <ItemTable
            rows={brand.items.map((item) => ({
              id: item.id,
              nameZh: item.nameZh,
              dataType: item.dataType,
              unit: item.unit,
              category: item.category,
              extra: item.brandSpecific ? '品牌专有' : (item.sources ?? []).join('、'),
            }))}
            extraLabel='说明'
          />
        </CardContent>
      </Card>
    </div>
  )
}

function ItemTable({
  rows,
  extraLabel,
}: {
  rows: Array<{ id: string; nameZh: string; dataType: string; unit: string; category: string; extra: string }>
  extraLabel: string
}) {
  return (
    <div className='overflow-auto'>
      <Table>
        <TableHeader>
          <TableRow>
            <TableHead>中文名</TableHead>
            <TableHead>Id</TableHead>
            <TableHead>分类</TableHead>
            <TableHead>类型</TableHead>
            <TableHead>单位</TableHead>
            <TableHead>{extraLabel}</TableHead>
          </TableRow>
        </TableHeader>
        <TableBody>
          {rows.length === 0 ? (
            <TableRow>
              <TableCell colSpan={6} className='text-muted-foreground'>
                没有匹配的数据项。
              </TableCell>
            </TableRow>
          ) : null}
          {rows.map((row) => (
            <TableRow key={row.id}>
              <TableCell>{row.nameZh}</TableCell>
              <TableCell className='font-mono text-xs'>{row.id}</TableCell>
              <TableCell>{categoryLabel(row.category)}</TableCell>
              <TableCell>{row.dataType}</TableCell>
              <TableCell>{row.unit || '—'}</TableCell>
              <TableCell className='max-w-64 whitespace-normal text-xs text-muted-foreground'>{row.extra || '—'}</TableCell>
            </TableRow>
          ))}
        </TableBody>
      </Table>
    </div>
  )
}
