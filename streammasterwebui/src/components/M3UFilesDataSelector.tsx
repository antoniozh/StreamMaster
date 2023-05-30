import React from 'react';
import * as StreamMasterApi from '../store/iptvApi';
import * as Hub from "../store/signlar_functions";
import DataSelector from '../features/dataSelector/DataSelector';
import { formatJSONDateString, getTopToolOptions } from '../common/common';
import { Toast } from 'primereact/toast';
import { type CheckboxChangeEvent } from 'primereact/checkbox';
import { Checkbox } from 'primereact/checkbox';
import NumberEditorBodyTemplate from './NumberEditorBodyTemplate';
import StringEditorBodyTemplate from './StringEditorBodyTemplate';
import { type ColumnMeta } from '../features/dataSelector/DataSelectorTypes';

const M3UFilesDataSelector = (props: M3UFilesDataSelectorProps) => {
  const toast = React.useRef<Toast>(null);

  const [selectedM3UFile, setSelectedM3UFile] = React.useState<StreamMasterApi.M3UFilesDto>({ id: 0, name: 'All' } as StreamMasterApi.M3UFilesDto);

  const m3UFilesQuery = StreamMasterApi.useM3UFilesGetM3UFilesQuery();

  React.useMemo(() => {
    if (props.value?.id !== undefined) {

      setSelectedM3UFile(props.value);
    }
  }, [props.value, setSelectedM3UFile]);

  const SetSelectedM3UFileChanged = React.useCallback((data: StreamMasterApi.M3UFilesDto) => {
    if (!data) {
      return;
    }

    if (props.value === undefined || props.value.id === data.id) {
      return;
    }

    setSelectedM3UFile(data);

    props.onChange?.(data);
  }, [props]);

  const onM3UUpdateClick = React.useCallback(async (id: number, auto?: boolean | null, days?: number | null, maxStreams?: number | null, name?: string | null, url?: string | null) => {

    if (id < 1) {
      return;
    }

    if (!auto && !days && !maxStreams && !name && !url) {
      return;
    }

    const tosend = {} as StreamMasterApi.UpdateM3UFileRequest;

    tosend.id = id;
    if (auto) {
      tosend.autoUpdate = auto;
    }

    if (days) {
      tosend.daysToUpdate = days;
    }

    if (name) {
      tosend.name = name;
    }

    if (maxStreams) {
      tosend.maxStreamCount = maxStreams;
    }

    if (url) {
      tosend.url = url;
    }

    await Hub.UpdateM3UFile(tosend)
      .then((returnData) => {
        if (toast.current) {
          if (returnData) {
            toast.current.show({
              detail: `M3U File Update Successful`,
              life: 3000,
              severity: 'success',
              summary: 'Successful',
            });
          } else {
            toast.current.show({
              detail: `M3U File Update Failed`,
              life: 3000,
              severity: 'error',
              summary: 'Error',
            });
          }
        }
      }).catch((e) => {
        if (toast.current) {
          toast.current.show({
            detail: `M3U File Update Failed`,
            life: 3000,
            severity: 'error',
            summary: 'Error ' + e.message,
          });
        }
      });

  }, [toast]);

  const lastDownloadedTemplate = React.useCallback((rowData: StreamMasterApi.M3UFilesDto) => {
    if (rowData.id === 0) {
      return (<div />);
    }

    return (
      <div className="flex justify-content-center ">
        {formatJSONDateString(rowData.lastDownloaded ?? '')}
      </div>
    );
  }, []);

  const nameEditorBodyTemplate = React.useCallback((rowData: StreamMasterApi.M3UFilesDto) => {
    if (rowData.id === 0) {
      return (
        <div className='p-0 relative'
          style={{
            ...{
              backgroundColor: 'var(--mask-bg)',
              overflow: 'hidden',
              textOverflow: 'ellipsis',
              whiteSpace: 'nowrap',
            },
          }}
        >
          {rowData.name}
        </div>
      )
    }

    return (
      <StringEditorBodyTemplate
        onChange={async (e) => {
          await onM3UUpdateClick(rowData.id, null, null, null, e)
        }}

        value={rowData.name}
      />
    )
  }, [onM3UUpdateClick]);

  const urlEditorBodyTemplate = React.useCallback((rowData: StreamMasterApi.M3UFilesDto) => {
    if (rowData.id === 0) {
      return (
        <div className='p-0 relative'
          style={{
            ...{
              backgroundColor: 'var(--mask-bg)',
              overflow: 'hidden',
              textOverflow: 'ellipsis',
              whiteSpace: 'nowrap',
            },
          }}
        >
          {rowData.url}
        </div>
      )
    }

    return (
      <StringEditorBodyTemplate
        onChange={async (e) => {
          await onM3UUpdateClick(rowData.id, null, null, null, null, e)
        }}

        value={rowData.url}
      />
    )
  }, [onM3UUpdateClick]);

  const maxStreamCountTemplate = React.useCallback((rowData: StreamMasterApi.M3UFilesDto) => {
    if (rowData.id === 0) {
      return (<div />);
    }

    return (
      <NumberEditorBodyTemplate
        onChange={async (e) => {
          await onM3UUpdateClick(rowData.id, null, null, e);
        }}
        prefix='Max '
        suffix=' streams'

        value={rowData.maxStreamCount}
      />
    );
  }, [onM3UUpdateClick]);

  const stationCountTemplate = React.useCallback((rowData: StreamMasterApi.M3UFilesDto) => {
    if (rowData.id === 0) {
      return (<div />);
    }

    return (
      <div>{rowData.stationCount}</div>
    );
  }, []);

  const targetActionBodyTemplate = React.useCallback((rowData: StreamMasterApi.M3UFilesDto) => {
    if (rowData.id === 0) {
      return (<div />);
    }

    return (
      <div className="dataselector p-inputgroup align-items-center">
        <Checkbox
          checked={rowData.autoUpdate}
          onChange={async (e: CheckboxChangeEvent) => {
            console.log(e.checked);
            await onM3UUpdateClick(rowData.id, e.checked ?? false);
          }
          }
          tooltip="Enable Auto Update"
          tooltipOptions={getTopToolOptions}
        />

        <NumberEditorBodyTemplate
          onChange={async (e) => {
            await onM3UUpdateClick(rowData.id, rowData.autoUpdate, e, rowData.maxStreamCount ?? 0);
          }}
          suffix=' days'
          value={rowData.daysToUpdate}
        />

      </div>
    );
  }, [onM3UUpdateClick]);

  const sourceColumns = React.useMemo((): ColumnMeta[] => {
    return [
      { bodyTemplate: nameEditorBodyTemplate, field: 'name', filter: true, header: 'Name', sortable: true },
      { bodyTemplate: lastDownloadedTemplate, field: 'lastDownloaded', header: 'Downloaded', sortable: true },
      { bodyTemplate: maxStreamCountTemplate, field: 'maxStreamCount', header: 'Max Streams', sortable: true },
      { bodyTemplate: stationCountTemplate, field: 'stationCount', header: 'Streams', sortable: true },
      { bodyTemplate: urlEditorBodyTemplate, field: 'url', sortable: true },
      {
        align: 'center', bodyTemplate: targetActionBodyTemplate, field: 'autoUpdate',
        style: {
          maxWidth: '8rem',
          width: '8rem',
        } as React.CSSProperties,
      },

    ]
  }, [lastDownloadedTemplate, maxStreamCountTemplate, nameEditorBodyTemplate, stationCountTemplate, targetActionBodyTemplate, urlEditorBodyTemplate]);

  return (
    <>
      <Toast position="bottom-right" ref={toast} />
      <DataSelector
        columns={sourceColumns}
        dataSource={m3UFilesQuery.data}
        emptyMessage="No M3U Files"
        globalSearchEnabled={false}
        id='m3ufilesdataselector'
        isLoading={m3UFilesQuery.isLoading}
        onSelectionChange={(e) =>
          SetSelectedM3UFileChanged(e as StreamMasterApi.M3UFilesDto)
        }
        selection={selectedM3UFile}
      />
    </>
  );
};

M3UFilesDataSelector.displayName = 'M3UFilesDataSelector';
type M3UFilesDataSelectorProps = {
  onChange?: (value: StreamMasterApi.M3UFilesDto) => void;
  value?: StreamMasterApi.M3UFilesDto | undefined;
};

export default React.memo(M3UFilesDataSelector);