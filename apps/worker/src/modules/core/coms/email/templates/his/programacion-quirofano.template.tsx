import { ProgramacionQuirofanoTemplateDto } from '@hsm-lib/definitions/dtos';

export function ProgramacionQuirofanoTemplate(
  data: ProgramacionQuirofanoTemplateDto,
): React.ReactNode {
  const pStyle: React.CSSProperties = {
    fontSize: '16px',
    lineHeight: '24px',
    margin: '0 0 16px 0',
    color: '#111827',
  };

  const tableStyle: React.CSSProperties = {
    width: '100%',
    borderCollapse: 'collapse',
    borderSpacing: 0,
    fontSize: '12px',
  };

  const thStyle: React.CSSProperties = {
    textAlign: 'center',
    padding: '10px 8px',
    backgroundColor: '#f3f5f7',
    borderBottom: '1px solid #e5e7eb',
    fontWeight: 800,
    letterSpacing: '0.2px',
    verticalAlign: 'middle',
  };

  const tdStyle: React.CSSProperties = {
    textAlign: 'center',
    padding: '10px 8px',
    borderBottom: '1px solid #e5e7eb',
    verticalAlign: 'top',
    color: '#111827',
  };

  const rowAltStyle: React.CSSProperties = {
    backgroundColor: '#fafafa',
  };

  const nowrapStyle: React.CSSProperties = {
    whiteSpace: 'nowrap',
  };

  return (
    <>
      <p style={pStyle}>
        Le saludamos del Hospital Santamaría. A continuación se detalla la
        programación de cirugías:
      </p>

      <table
        role="presentation"
        cellPadding={0}
        cellSpacing={0}
        style={tableStyle}
      >
        <thead>
          <tr>
            <th style={{ ...thStyle, width: '8%' }}>ID</th>
            <th style={{ ...thStyle, width: '10%' }}>FECHA</th>
            <th style={{ ...thStyle, width: '8%' }}>HORA</th>
            <th style={{ ...thStyle, width: '6%' }}>QX</th>
            <th style={{ ...thStyle, width: '10%' }}>HC</th>
            <th style={{ ...thStyle, width: '16%' }}>PACIENTE</th>
            <th style={{ ...thStyle, width: '22%' }}>PROCEDIMIENTO</th>
            <th style={{ ...thStyle, width: '14%' }}>CIRUJANO</th>
            <th style={{ ...thStyle, width: '16%' }}>OBSERVACIÓN</th>
          </tr>
        </thead>

        <tbody>
          {data.cirugias.map((c, idx) => (
            <tr
              key={`${c.surgeryId}-${c.surgeryDate}-${c.surgeryTime}-${idx}`}
              style={idx % 2 ? rowAltStyle : undefined}
            >
              <td style={{ ...tdStyle, ...nowrapStyle }}>{c.surgeryId}</td>
              <td style={{ ...tdStyle, ...nowrapStyle }}>{c.surgeryDate}</td>
              <td style={{ ...tdStyle, ...nowrapStyle }}>{c.surgeryTime}</td>
              <td style={{ ...tdStyle, ...nowrapStyle }}>
                {c.operatingRoomId}
              </td>
              <td style={{ ...tdStyle, ...nowrapStyle }}>
                {c.patientMedicalHistoryId}
              </td>
              <td style={tdStyle}>{c.patientName}</td>
              <td style={tdStyle}>{c.surgeryType}</td>
              <td style={tdStyle}>{c.surgeonName}</td>
              <td style={tdStyle}>{c.medicalObservations}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </>
  );
}
